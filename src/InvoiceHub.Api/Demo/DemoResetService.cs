using InvoiceHub.Infrastructure.Demo;
using Microsoft.Extensions.Options;

namespace InvoiceHub.Api.Demo;

/// <summary>
/// Demo mode only. Wipes what visitors created and recreates the demo data every Demo:ResetIntervalHours,
/// so the public demo cannot be trashed permanently. The first run is one interval after startup, because
/// the startup seed has just created fresh data.
/// </summary>
public sealed class DemoResetService(
    DemoDataSeeder seeder,
    IOptions<DemoOptions> options,
    ILogger<DemoResetService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(Math.Max(0.01, options.Value.ResetIntervalHours));
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await seeder.ResetAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Demo reset failed; it will try again in {Interval}", interval);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }
}
