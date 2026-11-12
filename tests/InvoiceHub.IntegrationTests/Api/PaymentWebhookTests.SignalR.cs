using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using InvoiceHub.Api.Hubs;
using InvoiceHub.Application.Abstractions;
using InvoiceHub.Application.Auth;
using InvoiceHub.Application.Payments;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InvoiceHub.IntegrationTests.Api;

/// <summary>The SignalR side: payments are pushed to the connected clients of the owning tenant only.</summary>
public sealed partial class PaymentWebhookTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    /// <summary>A connected client that collects the PaymentReceived messages it is sent.</summary>
    private sealed class HubClient(HubConnection connection) : IAsyncDisposable
    {
        public Channel<PaymentReceivedMessage> Messages { get; } = Channel.CreateUnbounded<PaymentReceivedMessage>();

        public async Task StartAsync()
        {
            connection.On<PaymentReceivedMessage>(nameof(IInvoiceClient.PaymentReceived), m => Messages.Writer.TryWrite(m));
            await connection.StartAsync();
        }

        public async Task<PaymentReceivedMessage> NextAsync()
        {
            using var timeout = new CancellationTokenSource(Patience);
            return await Messages.Reader.ReadAsync(timeout.Token);
        }

        /// <summary>True if nothing arrives within a short wait. Only meaningful after a message that should arrive did.</summary>
        public async Task<bool> StaysQuietAsync()
        {
            await Task.Delay(750);
            return !Messages.Reader.TryRead(out _);
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    private HubConnection BuildConnection(string? token, HttpTransportType transport = HttpTransportType.LongPolling, ApiFactory? host = null)
    {
        var server = (host ?? factory).Server;
        return new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, InvoiceEventsHub.Path), options =>
            {
                options.Transports = transport;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult(token);
                if (transport == HttpTransportType.WebSockets)
                {
                    options.SkipNegotiation = false;
                    // A browser cannot set headers on a WebSocket, so the JavaScript client puts the token in the
                    // query string. Do the same here, with no Authorization header, to test that path.
                    options.WebSocketFactory = async (context, cancellationToken) =>
                    {
                        var uri = new UriBuilder(context.Uri)
                        {
                            Query = context.Uri.Query.TrimStart('?') + "&access_token=" + Uri.EscapeDataString(token ?? ""),
                        }.Uri;
                        var socket = server.CreateWebSocketClient();
                        return await socket.ConnectAsync(uri, cancellationToken);
                    };
                }
            })
            .Build();
    }

    private async Task<HubClient> ConnectAsync(string token, HttpTransportType transport = HttpTransportType.LongPolling)
    {
        var client = new HubClient(BuildConnection(token, transport));
        await client.StartAsync();
        return client;
    }

    [Fact]
    public async Task A_payment_is_pushed_to_the_connected_clients_of_that_tenant()
    {
        var setup = await NewInvoiceAsync();
        await using var client = await ConnectAsync(setup.Auth.AccessToken);

        (await SendSignedAsync(Body(setup.Invoice.Id, 500m, eventId: "evt_push"))).EnsureSuccessStatusCode();

        var message = await client.NextAsync();
        Assert.Equal((setup.Invoice.Id, setup.Invoice.InvoiceNumber, "evt_push", 500m, 500m, 680m, "PartiallyPaid"),
            (message.InvoiceId, message.InvoiceNumber, message.EventId, message.Amount, message.AmountPaid, message.BalanceDue, message.InvoiceStatus));
    }

    [Fact]
    public async Task Another_tenants_clients_never_receive_it()
    {
        var owner = await NewInvoiceAsync();
        var other = await _client.RegisterAsync();
        await using var ownerClient = await ConnectAsync(owner.Auth.AccessToken);
        await using var otherClient = await ConnectAsync(other.AccessToken);

        (await SendSignedAsync(Body(owner.Invoice.Id, 500m))).EnsureSuccessStatusCode();

        await ownerClient.NextAsync(); // the owner got it, so the push has happened
        Assert.True(await otherClient.StaysQuietAsync());
    }

    [Fact]
    public async Task Every_connection_of_the_tenant_gets_it_including_staff()
    {
        var setup = await NewInvoiceAsync();
        var staffEmail = TestData.NewEmail();
        (await _client.PostAsync("/api/users", new CreateStaffUserRequest(staffEmail, TestData.Password), setup.Auth.AccessToken))
            .EnsureSuccessStatusCode();
        var staff = await _client.LoginAsync(staffEmail, TestData.Password);
        await using var admin = await ConnectAsync(setup.Auth.AccessToken);
        await using var staffClient = await ConnectAsync(staff.AccessToken);

        (await SendSignedAsync(Body(setup.Invoice.Id, 100m))).EnsureSuccessStatusCode();

        Assert.Equal(100m, (await admin.NextAsync()).Amount);
        Assert.Equal(100m, (await staffClient.NextAsync()).Amount);
    }

    [Fact]
    public async Task A_retried_event_and_a_rejected_request_push_nothing_more()
    {
        var setup = await NewInvoiceAsync();
        await using var client = await ConnectAsync(setup.Auth.AccessToken);
        var body = Body(setup.Invoice.Id, 100m, eventId: "evt_once");

        (await SendSignedAsync(body)).EnsureSuccessStatusCode();
        await client.NextAsync();
        (await SendSignedAsync(body)).EnsureSuccessStatusCode();                                   // retry
        var forged = await SendSignedAsync(Body(setup.Invoice.Id, 100m), secret: "wrong-secret-0123456789-abcdefghijklmno");

        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
        Assert.True(await client.StaysQuietAsync());
    }

    [Fact]
    public async Task Connecting_without_a_valid_token_is_refused()
    {
        await using var anonymous = new HubClient(BuildConnection(token: null));
        await using var garbage = new HubClient(BuildConnection(token: "not-a-real-token"));

        var first = await Assert.ThrowsAnyAsync<Exception>(anonymous.StartAsync);
        var second = await Assert.ThrowsAnyAsync<Exception>(garbage.StartAsync);

        Assert.Contains("401", first.Message);
        Assert.Contains("401", second.Message);
    }

    [Fact]
    public async Task A_WebSocket_connection_authenticates_with_the_token_in_the_query_string()
    {
        var setup = await NewInvoiceAsync();
        await using var client = await ConnectAsync(setup.Auth.AccessToken, HttpTransportType.WebSockets);

        (await SendSignedAsync(Body(setup.Invoice.Id, 250m))).EnsureSuccessStatusCode();

        Assert.Equal(250m, (await client.NextAsync()).Amount);
    }

    [Fact]
    public async Task The_query_string_token_is_accepted_for_the_hub_only_not_for_normal_endpoints()
    {
        var auth = await _client.RegisterAsync();

        var response = await _client.GetAsync($"/api/auth/me?access_token={auth.AccessToken}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_failing_push_does_not_undo_the_payment_or_fail_the_webhook()
    {
        await using var broken = new ApiFactory();
        await broken.InitializeAsync();
        using var brokenHost = broken.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IPaymentNotifier, ThrowingNotifier>())));
        var brokenClient = brokenHost.CreateClient();
        var auth = await brokenClient.RegisterAsync();
        var customer = await brokenClient.PostAsync("/api/customers",
            new InvoiceHub.Application.Customers.Commands.CreateCustomerCommand("Buyer", TestData.NewEmail(), null, "27", "1 Road", null, "Pune", "411001"),
            auth.AccessToken);
        var customerId = (await customer.Content.ReadFromJsonAsync<InvoiceHub.Application.Customers.CustomerDto>())!.Id;
        var invoice = (await (await brokenClient.PostAsync("/api/invoices",
            new InvoiceHub.Application.Invoices.Commands.CreateInvoice.CreateInvoiceCommand(customerId, "INV-BROKEN-1",
                new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), null,
                [new InvoiceHub.Application.Invoices.Commands.CreateInvoice.CreateInvoiceLine(null, "Work", "9983", 1, 1000m, 18m)]),
            auth.AccessToken)).Content.ReadFromJsonAsync<InvoiceHub.Application.Invoices.InvoiceDto>())!;
        (await brokenClient.PostAsync($"/api/invoices/{invoice.Id}/issue", new { }, auth.AccessToken)).EnsureSuccessStatusCode();

        var bytes = System.Text.Encoding.UTF8.GetBytes(Body(invoice.Id, 500m));
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var request = new HttpRequestMessage(HttpMethod.Post, Url) { Content = new ByteArrayContent(bytes) };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.Add(PaymentWebhookSignature.TimestampHeader, timestamp.ToString());
        request.Headers.Add(PaymentWebhookSignature.SignatureHeader, PaymentWebhookSignature.Sign(ApiFactory.WebhookSecret, timestamp, bytes));
        var response = await brokenClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = await brokenClient.GetAsync($"/api/invoices/{invoice.Id}", auth.AccessToken);
        Assert.Equal(500m, (await after.Content.ReadFromJsonAsync<InvoiceHub.Application.Invoices.InvoiceDto>())!.AmountPaid);
    }

    private sealed class ThrowingNotifier : IPaymentNotifier
    {
        public Task PaymentReceivedAsync(Guid tenantId, PaymentReceivedMessage message, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The push failed.");
    }
}
