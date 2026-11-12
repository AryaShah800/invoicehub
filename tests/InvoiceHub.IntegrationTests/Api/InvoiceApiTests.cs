using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using InvoiceHub.Application.Auth;
using InvoiceHub.Application.Invoices;
using InvoiceHub.Application.Invoices.Commands.CreateInvoice;
using InvoiceHub.Domain.Customers;
using InvoiceHub.Domain.Products;

namespace InvoiceHub.IntegrationTests.Api;

[Collection(ApiCollection.Name)]
public sealed class InvoiceApiTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    /// <summary>A registered business with one customer (Maharashtra, "27") and one product (1000.00 at 18% GST).</summary>
    private sealed record Business(AuthResponse Auth, Guid CustomerId, Guid ProductId);

    private async Task<Business> NewBusinessAsync(bool productActive = true)
    {
        var auth = await _client.RegisterAsync();

        await using var db = factory.CreateDbContext(auth.TenantId);
        var customer = new Customer(auth.TenantId, "Buyer Pvt Ltd", TestData.NewEmail(), "27",
            new Address("1 Main Road", null, "Pune", "411001"));
        var product = new Product(auth.TenantId, "Widget", "8471", 1000m, 18m);
        if (!productActive)
            product.Deactivate();
        db.AddRange(customer, product);
        await db.SaveChangesAsync();

        return new Business(auth, customer.Id, product.Id);
    }

    private static CreateInvoiceCommand NewInvoice(Business business, string? number = null, params CreateInvoiceLine[] lines) => new(
        business.CustomerId,
        number ?? $"INV-{Guid.NewGuid():N}"[..14],
        new DateOnly(2026, 11, 1),
        new DateOnly(2026, 11, 30),
        "Thanks for your business",
        lines.Length > 0 ? lines : [new CreateInvoiceLine(business.ProductId, null, null, 2, null, null)]);

    private async Task<InvoiceDto> CreateAsync(Business business, CreateInvoiceCommand? command = null)
    {
        var response = await _client.PostAsync("/api/invoices", command ?? NewInvoice(business), business.Auth.AccessToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<InvoiceDto>())!;
    }

    // ---- create ----

    [Fact]
    public async Task Creating_an_invoice_returns_201_a_location_and_the_stored_invoice()
    {
        var business = await NewBusinessAsync();
        var command = NewInvoice(business);

        var response = await _client.PostAsync("/api/invoices", command, business.Auth.AccessToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var invoice = (await response.Content.ReadFromJsonAsync<InvoiceDto>())!;
        Assert.EndsWith($"/api/invoices/{invoice.Id}", response.Headers.Location!.ToString());
        Assert.Equal("Draft", invoice.Status);
        Assert.Equal(command.InvoiceNumber, invoice.InvoiceNumber);
        Assert.Equal("Buyer Pvt Ltd", invoice.CustomerName);
        Assert.Equal("27", invoice.PlaceOfSupplyStateCode);
        Assert.Equal("Thanks for your business", invoice.Notes);
    }

    [Fact]
    public async Task Catalogue_lines_take_price_hsn_and_gst_rate_from_the_product()
    {
        var business = await NewBusinessAsync();
        var command = NewInvoice(business, lines: new CreateInvoiceLine(business.ProductId, null, null, 2, null, null));

        var invoice = await CreateAsync(business, command);

        var line = Assert.Single(invoice.Lines);
        Assert.Equal("Widget", line.Description);
        Assert.Equal("8471", line.HsnCode);
        Assert.Equal(1000m, line.UnitPrice);
        Assert.Equal(18m, line.GstRate);
        Assert.Equal(2000m, line.TaxableValue);
        Assert.Equal(business.ProductId, line.ProductId);
    }

    [Fact]
    public async Task Lines_keep_their_order_and_the_total_adds_up_after_a_database_round_trip()
    {
        var business = await NewBusinessAsync();
        var command = NewInvoice(business, lines:
        [
            new CreateInvoiceLine(null, "Setup fee", "9983", 1, 250.50m, 18m),
            new CreateInvoiceLine(business.ProductId, null, null, 2, null, null),
            new CreateInvoiceLine(null, "Support", "998313", 3, 500m, 5m),
        ]);
        var created = await CreateAsync(business, command);

        var response = await _client.GetAsync($"/api/invoices/{created.Id}", business.Auth.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fetched = (await response.Content.ReadFromJsonAsync<InvoiceDto>())!;
        Assert.Equal([1, 2, 3], fetched.Lines.Select(l => l.LineNumber));
        Assert.Equal(["Setup fee", "Widget", "Support"], fetched.Lines.Select(l => l.Description));
        Assert.Equal(250.50m + 2000m + 1500m, fetched.TaxableTotal);
    }

    [Fact]
    public async Task Creating_an_invoice_without_a_token_is_unauthorized()
    {
        var business = await NewBusinessAsync();

        var response = await _client.PostAsync("/api/invoices", NewInvoice(business));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- validation (the pipeline behavior) ----

    [Fact]
    public async Task An_invalid_request_is_a_400_listing_every_problem()
    {
        var business = await NewBusinessAsync();
        var command = NewInvoice(business, lines:
        [
            new CreateInvoiceLine(business.ProductId, null, null, 0, null, null),
            new CreateInvoiceLine(null, "", "12", 1, null, 7m),
        ]) with { DueDate = new DateOnly(2026, 10, 1) };

        var response = await _client.PostAsync("/api/invoices", command, business.Auth.AccessToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = json.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains(errors, e => e.Contains("DueDate"));
        Assert.Contains(errors, e => e.Contains("Quantity"));
        Assert.Contains(errors, e => e.Contains("Description"));
        Assert.Contains(errors, e => e.Contains("HsnCode"));
        Assert.Contains(errors, e => e.Contains("GstRate"));
    }

    [Fact]
    public async Task A_catalogue_line_cannot_override_the_price()
    {
        var business = await NewBusinessAsync();
        var command = NewInvoice(business, lines: new CreateInvoiceLine(business.ProductId, null, null, 1, 1m, null));

        var response = await _client.PostAsync("/api/invoices", command, business.Auth.AccessToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_invalid_request_creates_nothing()
    {
        var business = await NewBusinessAsync();
        var number = $"BAD-{Guid.NewGuid():N}"[..14];
        var bad = NewInvoice(business, number) with { DueDate = new DateOnly(2020, 1, 1) };
        await _client.PostAsync("/api/invoices", bad, business.Auth.AccessToken);

        // The same number is still free, so the rejected request left no invoice behind.
        var good = NewInvoice(business, number);
        var response = await _client.PostAsync("/api/invoices", good, business.Auth.AccessToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ---- business rules ----

    [Fact]
    public async Task A_duplicate_invoice_number_within_a_tenant_is_a_conflict()
    {
        var business = await NewBusinessAsync();
        var first = NewInvoice(business);
        await CreateAsync(business, first);

        var response = await _client.PostAsync("/api/invoices", first, business.Auth.AccessToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Two_tenants_can_use_the_same_invoice_number()
    {
        var acme = await NewBusinessAsync();
        var globex = await NewBusinessAsync();
        var number = $"SAME-{Guid.NewGuid():N}"[..14];

        await CreateAsync(acme, NewInvoice(acme, number));
        var response = await _client.PostAsync("/api/invoices", NewInvoice(globex, number), globex.Auth.AccessToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task An_inactive_product_violates_a_business_rule()
    {
        var business = await NewBusinessAsync(productActive: false);

        var response = await _client.PostAsync("/api/invoices", NewInvoice(business), business.Auth.AccessToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_customer_is_404()
    {
        var business = await NewBusinessAsync();
        var command = NewInvoice(business) with { CustomerId = Guid.NewGuid() };

        var response = await _client.PostAsync("/api/invoices", command, business.Auth.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_product_is_404()
    {
        var business = await NewBusinessAsync();
        var command = NewInvoice(business, lines: new CreateInvoiceLine(Guid.NewGuid(), null, null, 1, null, null));

        var response = await _client.PostAsync("/api/invoices", command, business.Auth.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- tenant isolation over HTTP ----

    [Fact]
    public async Task Another_tenants_invoice_is_a_404_not_a_403()
    {
        var acme = await NewBusinessAsync();
        var globex = await NewBusinessAsync();
        var acmeInvoice = await CreateAsync(acme);

        var asOwner = await _client.GetAsync($"/api/invoices/{acmeInvoice.Id}", acme.Auth.AccessToken);
        var asOther = await _client.GetAsync($"/api/invoices/{acmeInvoice.Id}", globex.Auth.AccessToken);

        Assert.Equal(HttpStatusCode.OK, asOwner.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, asOther.StatusCode);
    }

    [Fact]
    public async Task An_invoice_cannot_be_raised_for_another_tenants_customer()
    {
        var acme = await NewBusinessAsync();
        var globex = await NewBusinessAsync();
        var command = NewInvoice(globex) with { CustomerId = acme.CustomerId };

        var response = await _client.PostAsync("/api/invoices", command, globex.Auth.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_invoice_cannot_use_another_tenants_product()
    {
        var acme = await NewBusinessAsync();
        var globex = await NewBusinessAsync();
        var command = NewInvoice(globex, lines: new CreateInvoiceLine(acme.ProductId, null, null, 1, null, null));

        var response = await _client.PostAsync("/api/invoices", command, globex.Auth.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_staff_user_sees_their_tenants_invoices()
    {
        var business = await NewBusinessAsync();
        var invoice = await CreateAsync(business);
        var staffEmail = TestData.NewEmail();
        await _client.PostAsync("/api/users", new CreateStaffUserRequest(staffEmail, TestData.Password), business.Auth.AccessToken);
        var staff = await _client.LoginAsync(staffEmail, TestData.Password);

        var response = await _client.GetAsync($"/api/invoices/{invoice.Id}", staff.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_invoice_id_is_404_and_an_empty_one_is_400()
    {
        var business = await NewBusinessAsync();

        var unknown = await _client.GetAsync($"/api/invoices/{Guid.NewGuid()}", business.Auth.AccessToken);
        var empty = await _client.GetAsync($"/api/invoices/{Guid.Empty}", business.Auth.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }
}
