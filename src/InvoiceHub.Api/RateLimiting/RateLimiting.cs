using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace InvoiceHub.Api.RateLimiting;

public sealed class RateLimitSettings
{
    public const string SectionName = "RateLimiting";

    /// <summary>Register and login requests allowed per client address per minute.</summary>
    public int AuthPermitsPerMinute { get; set; } = 10;

    /// <summary>Payment webhook requests allowed per client address per minute. Generous: it is a machine caller.</summary>
    public int WebhookPermitsPerMinute { get; set; } = 300;
}

public static class RateLimitPolicies
{
    public const string Auth = "auth";
    public const string Webhook = "webhook";
}

public static class RateLimitingExtensions
{
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RateLimitSettings>(configuration.GetSection(RateLimitSettings.SectionName));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // The limits are read from the settings on each request, so they can be changed by configuration
            // and overridden in tests. Each client address gets its own window.
            options.AddPolicy(RateLimitPolicies.Auth, http => Window(http, s => s.AuthPermitsPerMinute, RateLimitPolicies.Auth));
            options.AddPolicy(RateLimitPolicies.Webhook, http => Window(http, s => s.WebhookPermitsPerMinute, RateLimitPolicies.Webhook));

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();

                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new ProblemDetails { Status = StatusCodes.Status429TooManyRequests, Title = "Too many requests. Try again shortly." },
                    cancellationToken);
            };
        });

        return services;
    }

    private static RateLimitPartition<string> Window(HttpContext http, Func<RateLimitSettings, int> permits, string policy)
    {
        var settings = http.RequestServices.GetRequiredService<IOptions<RateLimitSettings>>().Value;
        // Behind a proxy this is the client address only if forwarded headers are enabled (see the README).
        var client = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter($"{policy}:{client}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, permits(settings)),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    }
}
