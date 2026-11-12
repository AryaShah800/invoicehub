using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using InvoiceHub.Api.RateLimiting;
using InvoiceHub.Application.Auth;
using InvoiceHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace InvoiceHub.IntegrationTests.Api;

/// <summary>Health probes, rate limiting, HSTS, and closing hub connections when their token expires.</summary>
[Collection(ApiCollection.Name)]
public sealed class HardeningTests(ApiFactory factory)
{
    // ---- health probes ----

    [Fact]
    public async Task Health_probes_are_anonymous_and_report_only_a_status_word()
    {
        var client = factory.CreateClient();

        var live = await client.GetAsync("/health");
        var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal("Healthy", await live.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("Healthy", await ready.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Readiness_fails_with_503_and_no_details_when_the_database_is_unreachable()
    {
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<InvoiceHubDbContext>>();
            services.AddDbContext<InvoiceHubDbContext>(options => options.UseSqlServer(
                "Server=(localdb)\\MSSQLLocalDB;Database=InvoiceHub_Does_Not_Exist_0123;Connect Timeout=3;TrustServerCertificate=True"));
        }));
        var client = host.CreateClient();

        var ready = await client.GetAsync("/health/ready");
        var live = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        var body = await ready.Content.ReadAsStringAsync();
        Assert.Equal("Unhealthy", body);
        Assert.DoesNotContain("Does_Not_Exist", body);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode); // the process itself is fine
    }

    // ---- rate limiting ----

    private HttpClient ClientWithLimits(int authPerMinute = 100_000, int webhookPerMinute = 100_000) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Configure<RateLimitSettings>(settings =>
            {
                settings.AuthPermitsPerMinute = authPerMinute;
                settings.WebhookPermitsPerMinute = webhookPerMinute;
            }))).CreateClient();

    [Fact]
    public async Task Repeated_login_attempts_from_one_address_are_cut_off_with_429_and_Retry_After()
    {
        var client = ClientWithLimits(authPerMinute: 3);
        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? last = null;

        for (var i = 0; i < 5; i++)
        {
            last = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(TestData.NewEmail(), "wrong-password"));
            statuses.Add(last.StatusCode);
        }

        Assert.Equal(
            [HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests],
            statuses);
        Assert.True(last!.Headers.Contains("Retry-After"));
        Assert.Contains("Too many requests", await last.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Registration_shares_the_limit_so_an_attacker_cannot_mint_tenants_in_bulk()
    {
        var client = ClientWithLimits(authPerMinute: 2);

        var first = await client.PostAsJsonAsync("/api/auth/register", TestData.NewRegistration());
        var second = await client.PostAsJsonAsync("/api/auth/register", TestData.NewRegistration());
        var third = await client.PostAsJsonAsync("/api/auth/register", TestData.NewRegistration());

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }

    [Fact]
    public async Task The_auth_limit_does_not_affect_other_endpoints()
    {
        var client = ClientWithLimits(authPerMinute: 1);
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("a@test.example", "x"));
        var throttled = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("a@test.example", "x"));

        var health = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task The_webhook_has_its_own_limit_and_forged_requests_count_towards_it()
    {
        var client = ClientWithLimits(webhookPerMinute: 2);
        HttpRequestMessage Forged() => new(HttpMethod.Post, "/api/webhooks/payments") { Content = new StringContent("{}") };

        var first = await client.SendAsync(Forged());
        var second = await client.SendAsync(Forged());
        var third = await client.SendAsync(Forged());

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }

    // ---- HSTS ----

    [Fact]
    public async Task In_production_HTTPS_responses_tell_browsers_to_use_HTTPS_only()
    {
        using var host = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://invoicehub.test") });

        var response = await client.GetAsync("/health");

        Assert.True(response.Headers.TryGetValues("Strict-Transport-Security", out var values));
        Assert.Contains("max-age=", values!.Single());
    }

    // ---- SignalR connections end with their token ----

    [Fact]
    public async Task A_hub_connection_is_closed_when_its_token_expires()
    {
        // A token that is valid now and expires in 3 seconds, signed like a real one.
        var now = DateTime.UtcNow;
        var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "InvoiceHub",
            audience: "InvoiceHub.Clients",
            claims:
            [
                new Claim("sub", Guid.NewGuid().ToString()),
                new Claim(AppClaims.TenantId, Guid.NewGuid().ToString()),
                new Claim(AppClaims.Role, Roles.Staff),
            ],
            notBefore: now.AddMinutes(-1),
            expires: now.AddSeconds(3),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.SigningKey)), SecurityAlgorithms.HmacSha256)));

        var server = factory.Server;
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "/hubs/invoices"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
        var closed = new TaskCompletionSource();
        connection.Closed += _ => { closed.TrySetResult(); return Task.CompletedTask; };

        await connection.StartAsync();
        Assert.Equal(HubConnectionState.Connected, connection.State);

        var finished = await Task.WhenAny(closed.Task, Task.Delay(TimeSpan.FromSeconds(20)));

        Assert.Same(closed.Task, finished);
        Assert.Equal(HubConnectionState.Disconnected, connection.State);
    }
}
