using InvoiceHub.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace InvoiceHub.Api.Health;

/// <summary>Ready only when the database answers. The failure reason is logged, never sent to the caller.</summary>
public sealed class DatabaseHealthCheck(InvoiceHubDbContext db, ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The database health check failed.");
            return HealthCheckResult.Unhealthy();
        }
    }
}
