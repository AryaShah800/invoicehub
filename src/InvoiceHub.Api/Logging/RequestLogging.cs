using InvoiceHub.Application.Auth;
using Serilog.AspNetCore;
using Serilog.Events;

namespace InvoiceHub.Api.Logging;

/// <summary>
/// One structured log line per request. It carries the tenant and user ids, so a problem can be traced to one
/// business, and it never carries the query string (SignalR sends its token there), the request body, or
/// the user's email.
/// </summary>
public static class RequestLogging
{
    public static void Configure(RequestLoggingOptions options)
    {
        options.GetLevel = (http, _, exception) =>
            exception is not null || http.Response.StatusCode >= 500 ? LogEventLevel.Error
            : http.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose // probes would drown the log
            : LogEventLevel.Information;

        // Runs when the request finishes, so the user has been authenticated by then.
        options.EnrichDiagnosticContext = (diagnostics, http) =>
        {
            var tenantId = http.User.FindFirst(AppClaims.TenantId)?.Value;
            if (tenantId is not null)
                diagnostics.Set("TenantId", tenantId);

            var userId = http.User.FindFirst("sub")?.Value;
            if (userId is not null)
                diagnostics.Set("UserId", userId);
        };
    }
}
