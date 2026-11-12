using InvoiceHub.Application.Reports;
using InvoiceHub.Domain.Invoices;
using InvoiceHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Benchmarks;

/// <summary>
/// The stored procedure's query shape written in EF Core: sum per invoice and slab first, then count those
/// rows per slab. It shows how much of the procedure's speed-up comes from the query shape rather than
/// from Dapper or the stored procedure itself.
/// </summary>
internal sealed class EfTwoStageReader(InvoiceHubDbContext db)
{
    private static readonly InvoiceStatus[] Reportable = [InvoiceStatus.Issued, InvoiceStatus.PartiallyPaid, InvoiceStatus.Paid];

    public async Task<IReadOnlyList<GstSummaryRow>> GetSummaryAsync(Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var perInvoiceAndSlab = db.Invoices
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.IssueDate >= from && i.IssueDate <= to && Reportable.Contains(i.Status))
            .SelectMany(i => i.Lines.Select(l => new { InvoiceId = i.Id, l.GstRate, l.TaxableValue, l.CgstAmount, l.SgstAmount, l.IgstAmount }))
            .GroupBy(x => new { x.InvoiceId, x.GstRate })
            .Select(g => new
            {
                g.Key.GstRate,
                TaxableValue = g.Sum(x => x.TaxableValue),
                CgstAmount = g.Sum(x => x.CgstAmount),
                SgstAmount = g.Sum(x => x.SgstAmount),
                IgstAmount = g.Sum(x => x.IgstAmount),
            });

        var rows = await perInvoiceAndSlab
            .GroupBy(x => x.GstRate)
            .Select(g => new
            {
                GstRate = g.Key,
                InvoiceCount = g.Count(),
                TaxableValue = g.Sum(x => x.TaxableValue),
                CgstAmount = g.Sum(x => x.CgstAmount),
                SgstAmount = g.Sum(x => x.SgstAmount),
                IgstAmount = g.Sum(x => x.IgstAmount),
            })
            .OrderBy(r => r.GstRate)
            .ToListAsync(ct);

        return rows
            .Select(r => new GstSummaryRow(r.GstRate, r.InvoiceCount, r.TaxableValue, r.CgstAmount, r.SgstAmount, r.IgstAmount))
            .ToList();
    }
}
