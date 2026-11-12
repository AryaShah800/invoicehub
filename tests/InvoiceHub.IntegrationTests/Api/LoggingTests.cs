using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text;
using InvoiceHub.Application.Payments;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;

namespace InvoiceHub.IntegrationTests.Api;

/// <summary>What ends up in the logs: useful context, and nothing that should stay secret.</summary>
[Collection(ApiCollection.Name)]
public sealed class LoggingTests(ApiFactory factory)
{
    private sealed class CapturingSink : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);

        /// <summary>The message and every property of every event, as one searchable string.</summary>
        public string Everything() => string.Join("\n", Events.Select(e =>
            e.RenderMessage() + " " + string.Join(" ", e.Properties.Select(p => $"{p.Key}={p.Value}"))));
    }

    private (HttpClient Client, CapturingSink Sink) NewHost()
    {
        var sink = new CapturingSink();
        var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<ILogEventSink>(sink)));
        return (host.CreateClient(), sink);
    }

    private static string? Property(LogEvent logEvent, string name) =>
        logEvent.Properties.TryGetValue(name, out var value) ? value.ToString().Trim('"') : null;

    [Fact]
    public async Task A_SignalR_token_in_the_query_string_never_reaches_the_logs()
    {
        var (client, sink) = NewHost();
        var auth = await client.RegisterAsync();

        var response = await client.PostAsync($"/hubs/invoices/negotiate?negotiateVersion=1&access_token={auth.AccessToken}", null);

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains(sink.Events, e => Property(e, "RequestPath") == "/hubs/invoices/negotiate");
        Assert.DoesNotContain(auth.AccessToken, sink.Everything());
        Assert.DoesNotContain("access_token", sink.Everything());
    }

    [Fact]
    public async Task A_request_log_carries_the_tenant_and_user_ids_but_not_the_email()
    {
        var (client, sink) = NewHost();
        var auth = await client.RegisterAsync();

        (await client.GetAsync("/api/auth/me", auth.AccessToken)).EnsureSuccessStatusCode();

        var meRequest = Assert.Single(sink.Events, e => Property(e, "RequestPath") == "/api/auth/me");
        Assert.Equal(auth.TenantId.ToString(), Property(meRequest, "TenantId"));
        Assert.Equal(auth.UserId.ToString(), Property(meRequest, "UserId"));
        Assert.DoesNotContain(auth.Email, sink.Everything());
        Assert.DoesNotContain(auth.AccessToken, sink.Everything());
    }

    [Fact]
    public async Task A_rejected_webhook_is_logged_with_a_reason_but_not_the_signature_or_secret()
    {
        var (client, sink) = NewHost();
        var body = Encoding.UTF8.GetBytes("""{"eventId":"evt_log","invoiceId":"00000000-0000-0000-0000-000000000001","amount":1,"currency":"INR"}""");
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var forgedSignature = PaymentWebhookSignature.Sign("not-the-real-secret-0123456789-abcdefghijk", timestamp, body);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/payments") { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.Add(PaymentWebhookSignature.TimestampHeader, timestamp.ToString());
        request.Headers.Add(PaymentWebhookSignature.SignatureHeader, forgedSignature);

        var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        var rejected = Assert.Single(sink.Events, e => e.RenderMessage().Contains("Rejected a payment webhook"));
        Assert.Equal(LogEventLevel.Warning, rejected.Level);
        Assert.Contains("InvalidSignature", rejected.RenderMessage());
        Assert.DoesNotContain(forgedSignature, sink.Everything());
        Assert.DoesNotContain(ApiFactory.WebhookSecret, sink.Everything());
    }

    [Fact]
    public async Task Health_probes_do_not_flood_the_log_at_normal_levels()
    {
        var (client, sink) = NewHost();

        await client.GetAsync("/health");

        var loud = sink.Events
            .Where(e => e.RenderMessage().Contains("responded") && Property(e, "RequestPath") == "/health" && e.Level >= LogEventLevel.Information)
            .ToList();
        Assert.True(loud.Count == 0, string.Join(" | ", loud.Select(e => $"{e.Level}: {e.RenderMessage()}")));
    }
}
