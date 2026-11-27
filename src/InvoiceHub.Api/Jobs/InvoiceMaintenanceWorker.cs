using InvoiceHub.Application.Abstractions;
using InvoiceHub.Application.Maintenance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InvoiceHub.Api.Jobs;

public sealed class BackgroundJobOptions
{
    public const string Section = "BackgroundJobs";

    public bool Enabled { get; set; } = true;

    /// <summary>How often the job wakes up. Running more often is harmless because every step is idempotent.</summary>
    public int IntervalMinutes { get; set; } = 60;
}

/// <summary>
/// Hosted service that, for every active tenant, flags newly overdue invoices and raises due recurring invoices.
/// There is no signed-in user here, so each unit of work gets its own DI scope with the tenant assigned up front.
/// That keeps the tenant query filters and the write guard in force, and one failing tenant or template cannot
/// stop the others.
/// </summary>
public sealed class InvoiceMaintenanceWorker(
    IServiceScopeFactory scopes,
    TimeProvider time,
    IBusinessClock clock,
    IOptions<BackgroundJobOptions> options,
    ILogger<InvoiceMaintenanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Background jobs are disabled (BackgroundJobs:Enabled=false)");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, settings.IntervalMinutes));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(Today(), stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Background job run failed; it will try again in {Interval}", interval);
            }

            try
            {
                await Task.Delay(interval, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public DateOnly Today() => clock.Today();

    /// <summary>One pass over every active tenant. Public so tests and tools can run it for a chosen date.</summary>
    public async Task<MaintenanceResult> RunOnceAsync(DateOnly today, CancellationToken ct)
    {
        List<Guid> tenantIds;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            tenantIds = await db.Tenants.Where(t => t.IsActive).Select(t => t.Id).ToListAsync(ct);
        }

        int overdue = 0, generated = 0;
        foreach (var tenantId in tenantIds)
        {
            overdue += await InTenantAsync(tenantId, "mark overdue", (svc, token) => svc.MarkOverdueAsync(today, token), ct);

            var due = await InTenantAsync<IReadOnlyList<Guid>>(tenantId, "list recurring",
                (svc, token) => svc.ListDueTemplatesAsync(today, token), ct) ?? [];
            foreach (var templateId in due)
                generated += await InTenantAsync(tenantId, "generate recurring", (svc, token) => svc.GenerateAsync(templateId, today, token), ct);
        }

        if (overdue > 0 || generated > 0)
            logger.LogInformation("Background job marked {Overdue} invoices overdue and generated {Generated} recurring invoices", overdue, generated);
        return new MaintenanceResult(overdue, generated);
    }

    private async Task<T?> InTenantAsync<T>(Guid tenantId, string step,
        Func<InvoiceMaintenanceService, CancellationToken, Task<T>> work, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ITenantAssigner>().Assign(tenantId);
            return await work(scope.ServiceProvider.GetRequiredService<InvoiceMaintenanceService>(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Background step {Step} failed for tenant {TenantId}", step, tenantId);
            return default;
        }
    }
}
