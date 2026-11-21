using InvoiceHub.Application.Abstractions;
using InvoiceHub.Domain.Invoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InvoiceHub.Application.Maintenance;

public sealed record MaintenanceResult(int OverdueMarked, int InvoicesGenerated);

/// <summary>
/// The work of the background job for ONE tenant (the tenant of the current scope): note invoices that went past
/// their due date, and raise the invoices that recurring templates owe. The date is passed in so tests can pick
/// any day, and every step is safe to repeat: an invoice is flagged overdue once, and an occurrence is generated
/// once (its number is deterministic and unique, and the template moves to its next date in the same save).
/// </summary>
public sealed class InvoiceMaintenanceService(
    IApplicationDbContext db,
    ITenantProvider tenantProvider,
    IInvoiceEventNotifier notifier,
    ILogger<InvoiceMaintenanceService> logger)
{
    public const int BatchSize = 200;

    /// <summary>A template that was paused for months catches up at most this many occurrences per run.</summary>
    public const int MaxCatchUpPerRun = 12;

    private Guid TenantId => tenantProvider.TenantId
        ?? throw new InvalidOperationException("Background work needs a current tenant.");

    public async Task<MaintenanceResult> RunAsync(DateOnly today, CancellationToken ct)
    {
        var overdue = await MarkOverdueAsync(today, ct);
        var generated = 0;
        foreach (var templateId in await ListDueTemplatesAsync(today, ct))
            generated += await GenerateAsync(templateId, today, ct);
        return new MaintenanceResult(overdue, generated);
    }

    public async Task<int> MarkOverdueAsync(DateOnly today, CancellationToken ct)
    {
        var total = 0;
        while (true)
        {
            var batch = await db.Invoices
                .Include(i => i.Lines)
                .Where(i => (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid)
                            && i.DueDate < today && i.OverdueMarkedOn == null)
                .OrderBy(i => i.DueDate).ThenBy(i => i.InvoiceNumber)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0)
                return total;

            var customerIds = batch.Select(i => i.CustomerId).Distinct().ToList();
            var names = await db.Customers.Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);

            var marked = batch.Where(i => i.MarkOverdue(today)).ToList();
            await db.SaveChangesAsync(ct);
            total += marked.Count;

            foreach (var invoice in marked)
            {
                await NotifySafelyAsync(() => notifier.InvoiceOverdueAsync(TenantId, new InvoiceOverdueMessage(
                    invoice.Id, invoice.InvoiceNumber, names.GetValueOrDefault(invoice.CustomerId, ""), invoice.DueDate, invoice.BalanceDue), ct));
            }
            if (batch.Count < BatchSize)
                return total;
        }
    }

    public async Task<IReadOnlyList<Guid>> ListDueTemplatesAsync(DateOnly today, CancellationToken ct) =>
        await db.RecurringInvoices
            .Where(r => r.IsActive && r.NextRunDate <= today && (r.EndDate == null || r.NextRunDate <= r.EndDate))
            .OrderBy(r => r.NextRunDate)
            .Select(r => r.Id)
            .ToListAsync(ct);

    /// <summary>Generates every due occurrence of one template (up to the catch-up limit) and saves once.</summary>
    public async Task<int> GenerateAsync(Guid templateId, DateOnly today, CancellationToken ct)
    {
        var template = await db.RecurringInvoices.Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == templateId, ct);
        if (template is null || !template.IsDue(today))
            return 0;

        var sellerState = await db.Tenants.Where(t => t.Id == TenantId).Select(t => t.StateCode).FirstAsync(ct);
        var customer = await db.Customers.FirstAsync(c => c.Id == template.CustomerId, ct);
        var productIds = template.Lines.Select(l => l.ProductId).ToList();
        var products = await db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var created = new List<Invoice>();
        for (var run = 0; run < MaxCatchUpPerRun && template.IsDue(today); run++)
        {
            var occurrence = template.NextRunDate;
            var number = template.NumberFor(occurrence);
            if (!await db.Invoices.AnyAsync(i => i.InvoiceNumber == number, ct))
            {
                var invoice = new Invoice(TenantId, sellerState, customer, number, occurrence, occurrence.AddDays(template.DueInDays));
                invoice.SetNotes($"Recurring invoice: {template.Name}");
                foreach (var line in template.Lines)
                {
                    if (!products.TryGetValue(line.ProductId, out var product) || !product.IsActive)
                        throw new InvalidOperationException($"Recurring invoice '{template.Name}' uses a product that is missing or inactive.");
                    invoice.AddLine(product, line.Quantity);
                }
                invoice.Issue();
                db.Invoices.Add(invoice);
                created.Add(invoice);
            }
            template.Advance();
        }

        await db.SaveChangesAsync(ct);

        foreach (var invoice in created)
        {
            await NotifySafelyAsync(() => notifier.RecurringInvoiceGeneratedAsync(TenantId, new RecurringInvoiceGeneratedMessage(
                template.Id, template.Name, invoice.Id, invoice.InvoiceNumber, invoice.GrandTotal), ct));
        }
        return created.Count;
    }

    // A failed push must never undo or repeat work that is already saved.
    private async Task NotifySafelyAsync(Func<Task> send)
    {
        try
        {
            await send();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not send a background job notification");
        }
    }
}
