using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using InvoiceHub.Application.Auth;
using InvoiceHub.Application.Customers;
using InvoiceHub.Application.Customers.Commands;
using InvoiceHub.Application.Invoices;
using InvoiceHub.Application.Invoices.Commands.CreateInvoice;
using InvoiceHub.Application.Payments;
using InvoiceHub.Application.Products;
using InvoiceHub.Application.Products.Commands;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.IntegrationTests.Api;

[Collection(ApiCollection.Name)]
public sealed partial class PaymentWebhookTests(ApiFactory factory)
{
    private const string Url = "/api/webhooks/payments";
    private readonly HttpClient _client = factory.CreateClient();

    /// <summary>One issued invoice for 1000.00 plus 18% GST in the seller's own state: 1180.00 due.</summary>
    private sealed record Setup(AuthResponse Auth, InvoiceDto Invoice);

    private async Task<Setup> NewInvoiceAsync(bool issue = true)
    {
        var auth = await _client.RegisterAsync();
        var customer = (await (await _client.PostAsync("/api/customers",
            new CreateCustomerCommand("Buyer Pvt Ltd", TestData.NewEmail(), null, "27", "1 Main Road", null, "Pune", "411001"),
            auth.AccessToken)).Content.ReadFromJsonAsync<CustomerDto>())!;
        var product = (await (await _client.PostAsync("/api/products",
            new CreateProductCommand("Widget", "8471", 1000m, 18m), auth.AccessToken)).Content.ReadFromJsonAsync<ProductDto>())!;

        var command = new CreateInvoiceCommand(customer.Id, $"INV-{Guid.NewGuid():N}"[..14], new DateOnly(2026, 11, 1),
            new DateOnly(2026, 11, 30), null, [new CreateInvoiceLine(product.Id, null, null, 1, null, null)]);
        var invoice = (await (await _client.PostAsync("/api/invoices", command, auth.AccessToken))
            .Content.ReadFromJsonAsync<InvoiceDto>())!;

        if (issue)
        {
            var issued = await _client.PostAsync($"/api/invoices/{invoice.Id}/issue", new { }, auth.AccessToken);
            invoice = (await issued.Content.ReadFromJsonAsync<InvoiceDto>())!;
        }

        return new Setup(auth, invoice);
    }

    private static string Body(Guid invoiceId, decimal amount, string? eventId = null, string currency = "INR") =>
        JsonSerializer.Serialize(new
        {
            eventId = eventId ?? $"evt_{Guid.NewGuid():N}",
            invoiceId,
            amount,
            currency,
            reference = "UPI-REF-1",
        });

    /// <summary>Sends the body the way the provider would: signed with the shared secret and a fresh timestamp.</summary>
    private Task<HttpResponseMessage> SendSignedAsync(
        string body, string secret = ApiFactory.WebhookSecret, DateTimeOffset? at = null, string? bearer = null)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var timestamp = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        return SendAsync(bytes, timestamp.ToString(), PaymentWebhookSignature.Sign(secret, timestamp, bytes), bearer);
    }

    private async Task<HttpResponseMessage> SendAsync(byte[] bytes, string? timestamp, string? signature, string? bearer = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url) { Content = new ByteArrayContent(bytes) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (timestamp is not null)
            request.Headers.Add(PaymentWebhookSignature.TimestampHeader, timestamp);
        if (signature is not null)
            request.Headers.Add(PaymentWebhookSignature.SignatureHeader, signature);
        if (bearer is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await _client.SendAsync(request);
    }

    private async Task<InvoiceDto> GetInvoiceAsync(Setup setup)
    {
        var response = await _client.GetAsync($"/api/invoices/{setup.Invoice.Id}", setup.Auth.AccessToken);
        return (await response.Content.ReadFromJsonAsync<InvoiceDto>())!;
    }

    private async Task<int> PaymentCountAsync(Setup setup)
    {
        await using var db = factory.CreateDbContext(setup.Auth.TenantId);
        return await db.Payments.CountAsync();
    }

    // ---- applying payments ----

    [Fact]
    public async Task A_signed_part_payment_is_applied_and_the_invoice_becomes_partially_paid()
    {
        var setup = await NewInvoiceAsync();
        Assert.Equal(1180m, setup.Invoice.BalanceDue);

        var response = await SendSignedAsync(Body(setup.Invoice.Id, 500m));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<RecordPaymentResult>())!;
        Assert.False(result.Duplicate);
        Assert.Equal(("PartiallyPaid", 500m, 680m), (result.InvoiceStatus, result.AmountPaid, result.BalanceDue));

        var invoice = await GetInvoiceAsync(setup);
        Assert.Equal(("PartiallyPaid", 500m, 680m), (invoice.Status, invoice.AmountPaid, invoice.BalanceDue));
        Assert.Equal(1, await PaymentCountAsync(setup));
    }

    [Fact]
    public async Task Paying_the_whole_balance_in_two_payments_makes_the_invoice_paid()
    {
        var setup = await NewInvoiceAsync();

        (await SendSignedAsync(Body(setup.Invoice.Id, 500m))).EnsureSuccessStatusCode();
        (await SendSignedAsync(Body(setup.Invoice.Id, 680m))).EnsureSuccessStatusCode();

        var invoice = await GetInvoiceAsync(setup);
        Assert.Equal(("Paid", 1180m, 0m), (invoice.Status, invoice.AmountPaid, invoice.BalanceDue));
    }

    [Fact]
    public async Task A_retried_event_is_acknowledged_but_not_applied_twice()
    {
        var setup = await NewInvoiceAsync();
        var body = Body(setup.Invoice.Id, 500m, eventId: "evt_retry_me");

        var first = await SendSignedAsync(body);
        var retry = await SendSignedAsync(body);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.True((await retry.Content.ReadFromJsonAsync<RecordPaymentResult>())!.Duplicate);
        Assert.Equal(500m, (await GetInvoiceAsync(setup)).AmountPaid);
        Assert.Equal(1, await PaymentCountAsync(setup));
    }

    [Fact]
    public async Task Simultaneous_deliveries_of_one_event_are_all_acknowledged_and_applied_once()
    {
        var setup = await NewInvoiceAsync();
        var body = Body(setup.Invoice.Id, 500m, eventId: "evt_raced");

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => SendSignedAsync(body)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var results = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<RecordPaymentResult>()));
        Assert.Equal(1, results.Count(r => !r!.Duplicate));
        Assert.Equal(500m, (await GetInvoiceAsync(setup)).AmountPaid);
        Assert.Equal(1, await PaymentCountAsync(setup));
    }

    [Fact]
    public async Task An_event_id_cannot_be_reused_for_a_different_invoice_of_the_same_tenant()
    {
        var first = await NewInvoiceAsync();
        var customerId = first.Invoice.CustomerId;
        var productId = first.Invoice.Lines[0].ProductId;
        var command = new CreateInvoiceCommand(customerId, $"INV-{Guid.NewGuid():N}"[..14], new DateOnly(2026, 11, 2),
            new DateOnly(2026, 11, 30), null, [new CreateInvoiceLine(productId, null, null, 1, null, null)]);
        var second = (await (await _client.PostAsync("/api/invoices", command, first.Auth.AccessToken))
            .Content.ReadFromJsonAsync<InvoiceDto>())!;
        (await _client.PostAsync($"/api/invoices/{second.Id}/issue", new { }, first.Auth.AccessToken)).EnsureSuccessStatusCode();
        (await SendSignedAsync(Body(first.Invoice.Id, 100m, eventId: "evt_shared_same_tenant"))).EnsureSuccessStatusCode();

        var response = await SendSignedAsync(Body(second.Id, 100m, eventId: "evt_shared_same_tenant"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var secondNow = await _client.GetAsync($"/api/invoices/{second.Id}", first.Auth.AccessToken);
        Assert.Equal(0m, (await secondNow.Content.ReadFromJsonAsync<InvoiceDto>())!.AmountPaid);
    }

    [Fact]
    public async Task Different_tenants_can_each_have_an_event_with_the_same_id()
    {
        var one = await NewInvoiceAsync();
        var two = await NewInvoiceAsync();

        (await SendSignedAsync(Body(one.Invoice.Id, 100m, eventId: "evt_shared"))).EnsureSuccessStatusCode();
        var response = await SendSignedAsync(Body(two.Invoice.Id, 100m, eventId: "evt_shared"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(100m, (await GetInvoiceAsync(two)).AmountPaid);
    }

    // ---- rejecting forged or stale requests ----

    [Fact]
    public async Task A_request_signed_with_the_wrong_secret_is_401_and_changes_nothing()
    {
        var setup = await NewInvoiceAsync();

        var response = await SendSignedAsync(Body(setup.Invoice.Id, 500m), secret: "an-attacker-guessed-secret-0123456789");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0m, (await GetInvoiceAsync(setup)).AmountPaid);
        Assert.Equal(0, await PaymentCountAsync(setup));
    }

    [Fact]
    public async Task A_body_changed_after_signing_is_401()
    {
        var setup = await NewInvoiceAsync();
        var signedBody = Encoding.UTF8.GetBytes(Body(setup.Invoice.Id, 1m, eventId: "evt_t"));
        var tamperedBody = Encoding.UTF8.GetBytes(Body(setup.Invoice.Id, 1180m, eventId: "evt_t"));
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var response = await SendAsync(tamperedBody, timestamp.ToString(),
            PaymentWebhookSignature.Sign(ApiFactory.WebhookSecret, timestamp, signedBody));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0m, (await GetInvoiceAsync(setup)).AmountPaid);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Missing_signature_headers_are_401(bool sendTimestamp, bool sendSignature)
    {
        var setup = await NewInvoiceAsync();
        var bytes = Encoding.UTF8.GetBytes(Body(setup.Invoice.Id, 500m));
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var response = await SendAsync(bytes,
            sendTimestamp ? timestamp.ToString() : null,
            sendSignature ? PaymentWebhookSignature.Sign(ApiFactory.WebhookSecret, timestamp, bytes) : null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0m, (await GetInvoiceAsync(setup)).AmountPaid);
    }

    [Fact]
    public async Task A_correctly_signed_request_with_an_old_timestamp_is_rejected_as_a_replay()
    {
        var setup = await NewInvoiceAsync();

        var response = await SendSignedAsync(Body(setup.Invoice.Id, 500m), at: DateTimeOffset.UtcNow.AddMinutes(-10));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0m, (await GetInvoiceAsync(setup)).AmountPaid);
    }

    [Fact]
    public async Task A_signed_webhook_is_applied_to_the_invoice_owner_even_if_a_user_token_is_attached()
    {
        var owner = await NewInvoiceAsync();
        var bystander = await _client.RegisterAsync();

        var response = await SendSignedAsync(Body(owner.Invoice.Id, 500m), bearer: bystander.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(500m, (await GetInvoiceAsync(owner)).AmountPaid);
        await using var db = factory.CreateDbContext(bystander.TenantId);
        Assert.Equal(0, await db.Payments.CountAsync());
    }

    // ---- rules and bad input ----

    [Fact]
    public async Task An_unknown_invoice_is_404()
    {
        var response = await SendSignedAsync(Body(Guid.NewGuid(), 500m));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_payment_larger_than_the_balance_is_422_and_changes_nothing()
    {
        var setup = await NewInvoiceAsync();

        var response = await SendSignedAsync(Body(setup.Invoice.Id, 1180.01m));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(0m, (await GetInvoiceAsync(setup)).AmountPaid);
        Assert.Equal(0, await PaymentCountAsync(setup));
    }

    [Fact]
    public async Task A_draft_invoice_cannot_be_paid()
    {
        var setup = await NewInvoiceAsync(issue: false);

        var response = await SendSignedAsync(Body(setup.Invoice.Id, 100m));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Draft", (await GetInvoiceAsync(setup)).Status);
    }

    [Theory]
    [InlineData("""{"eventId":"evt_1","invoiceId":"not-a-guid","amount":10,"currency":"INR"}""")]
    [InlineData("""[1,2,3]""")]
    [InlineData("""this is not json""")]
    public async Task A_signed_but_malformed_payload_is_400(string body)
    {
        var response = await SendSignedAsync(body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_fields_are_400()
    {
        var setup = await NewInvoiceAsync();

        var noEventId = await SendSignedAsync($$"""{"invoiceId":"{{setup.Invoice.Id}}","amount":10,"currency":"INR"}""");
        var negative = await SendSignedAsync(Body(setup.Invoice.Id, -10m));
        var dollars = await SendSignedAsync(Body(setup.Invoice.Id, 10m, currency: "USD"));
        var paise = await SendSignedAsync(Body(setup.Invoice.Id, 10.005m));

        Assert.All([noEventId, negative, dollars, paise], r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.Equal(0, await PaymentCountAsync(setup));
    }

    [Fact]
    public async Task A_body_over_the_size_limit_is_413_even_when_correctly_signed()
    {
        var huge = new string('x', 70 * 1024);

        var response = await SendSignedAsync($$"""{"eventId":"evt_1","reference":"{{huge}}"}""");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    // ---- races ----

    [Fact]
    public async Task Simultaneous_payments_never_take_more_than_the_balance_or_lose_track_of_money()
    {
        var setup = await NewInvoiceAsync();

        // Six 300.00 payments against 1180.00 due: at most three can fit (900.00). Four would be 1200.00.
        var responses = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => SendSignedAsync(Body(setup.Invoice.Id, 300m))));

        var applied = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(responses, r => Assert.Contains(r.StatusCode,
            [HttpStatusCode.OK, HttpStatusCode.Conflict, HttpStatusCode.UnprocessableEntity]));
        Assert.InRange(applied, 1, 3);

        var invoice = await GetInvoiceAsync(setup);
        Assert.Equal(300m * applied, invoice.AmountPaid);
        Assert.Equal(applied, await PaymentCountAsync(setup));
        Assert.True(invoice.AmountPaid <= invoice.GrandTotal);
    }
}
