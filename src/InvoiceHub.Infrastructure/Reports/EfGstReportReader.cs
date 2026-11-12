using InvoiceHub.Application.Reports;
using InvoiceHub.Domain.Invoices;
using InvoiceHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Infrastructure.Reports;

/// <summary>
/// The same report written as an EF Core LINQ query. It is not what the API uses: it exists as the
/// reference that the stored procedure is checked against and as the baseline in the benchmark.
/// The tenant query filter applies here, so the tenant id argument is only a consistency check.
/// </summary>
public sealed class EfGstReportReader(InvoiceHubDbContext db) : IGstReportReader
{
    private static readonly InvoiceStatus[] Reportable = [InvoiceStatus.Issued, InvoiceStatus.PartiallyPaid, InvoiceStatus.Paid];

    public async Task<IReadOnlyList<GstSummaryRow>> GetSummaryAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var rows = await db.Invoices
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.IssueDate >= from && i.IssueDate <= to && Reportable.Contains(i.Status))
            .SelectMany(i => i.Lines.Select(l => new { InvoiceId = i.Id, l.GstRate, l.TaxableValue, l.CgstAmount, l.SgstAmount, l.IgstAmount }))
            .GroupBy(x => x.GstRate)
            .Select(g => new
            {
                GstRate = g.Key,
                InvoiceCount = g.Select(x => x.InvoiceId).Distinct().Count(),
                TaxableValue = g.Sum(x => x.TaxableValue),
                CgstAmount = g.Sum(x => x.CgstAmount),
                SgstAmount = g.Sum(x => x.SgstAmount),
                IgstAmount = g.Sum(x => x.IgstAmount),
            })
            .OrderBy(r => r.GstRate)
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new GstSummaryRow(r.GstRate, r.InvoiceCount, r.TaxableValue, r.CgstAmount, r.SgstAmount, r.IgstAmount))
            .ToList();
    }
}
