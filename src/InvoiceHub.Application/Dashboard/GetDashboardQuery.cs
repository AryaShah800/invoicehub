using InvoiceHub.Application.Abstractions;
using InvoiceHub.Domain.Invoices;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Application.Dashboard;

/// <summary>Billed value for one calendar month, from invoices issued in that month (draft and cancelled left out).</summary>
public sealed record MonthlyRevenue(int Year, int Month, int InvoiceCount, decimal Taxable, decimal Tax, decimal Total);

public sealed record DashboardSummary(
    IReadOnlyList<MonthlyRevenue> Months,
    decimal Cgst,
    decimal Sgst,
    decimal Igst,
    int OverdueCount,
    decimal OverdueAmount,
    decimal OutstandingAmount,
    int DraftCount);

/// <summary>
/// Numbers for the dashboard: the last <paramref name="Months"/> calendar months (oldest first, empty months included),
/// the CGST/SGST/IGST split over that window, and what is overdue or outstanding right now.
/// </summary>
public sealed record GetDashboardQuery(int Months = 12) : IRequest<DashboardSummary>;

public sealed class GetDashboardQueryHandler(IApplicationDbContext db, TimeProvider clock)
    : IRequestHandler<GetDashboardQuery, DashboardSummary>
{
    public async Task<DashboardSummary> Handle(GetDashboardQuery request, CancellationToken cancellationToken)
    {
        var months = Math.Clamp(request.Months, 1, 36);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var first = new DateOnly(today.Year, today.Month, 1).AddMonths(-(months - 1));

        // Tenant query filter applies. Totals are computed from lines, so invoices are loaded with their lines.
        var billed = await db.Invoices.AsNoTracking().Include(i => i.Lines)
            .Where(i => i.Status != InvoiceStatus.Draft && i.Status != InvoiceStatus.Cancelled && i.IssueDate >= first)
            .ToListAsync(cancellationToken);

        var series = Enumerable.Range(0, months).Select(n =>
        {
            var m = first.AddMonths(n);
            var inMonth = billed.Where(i => i.IssueDate.Year == m.Year && i.IssueDate.Month == m.Month).ToList();
            return new MonthlyRevenue(m.Year, m.Month, inMonth.Count,
                inMonth.Sum(i => i.TaxableTotal), inMonth.Sum(i => i.TaxTotal), inMonth.Sum(i => i.GrandTotal));
        }).ToList();

        var open = await db.Invoices.AsNoTracking().Include(i => i.Lines)
            .Where(i => i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid)
            .ToListAsync(cancellationToken);
        var overdue = open.Where(i => i.DueDate < today).ToList();
        var drafts = await db.Invoices.CountAsync(i => i.Status == InvoiceStatus.Draft, cancellationToken);

        return new DashboardSummary(
            series,
            billed.Sum(i => i.CgstTotal), billed.Sum(i => i.SgstTotal), billed.Sum(i => i.IgstTotal),
            overdue.Count, overdue.Sum(i => i.BalanceDue), open.Sum(i => i.BalanceDue), drafts);
    }
}
