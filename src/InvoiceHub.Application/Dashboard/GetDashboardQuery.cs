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

public sealed class GetDashboardQueryHandler(IApplicationDbContext db, IBusinessClock clock)
    : IRequestHandler<GetDashboardQuery, DashboardSummary>
{
    public async Task<DashboardSummary> Handle(GetDashboardQuery request, CancellationToken cancellationToken)
    {
        var months = Math.Clamp(request.Months, 1, 36);
        var today = clock.Today();
        var first = new DateOnly(today.Year, today.Month, 1).AddMonths(-(months - 1));

        // Tenant query filter applies. Totals are sums over lines, so read only the numbers each line needs
        // instead of whole invoice and line entities (with their descriptions). The sums run here, not in SQL,
        // because the SQLite provider used by the hosted demo cannot SUM decimal columns.
        var billedLines = await db.Invoices.AsNoTracking()
            .Where(i => i.Status != InvoiceStatus.Draft && i.Status != InvoiceStatus.Cancelled && i.IssueDate >= first)
            // Two-argument SelectMany becomes an INNER JOIN; the lambda form needs APPLY, which SQLite lacks.
            .SelectMany(i => i.Lines, (i, l) => new BilledLine(
                i.Id, i.IssueDate, l.TaxableValue, l.CgstAmount, l.SgstAmount, l.IgstAmount))
            .ToListAsync(cancellationToken);

        var series = Enumerable.Range(0, months).Select(n =>
        {
            var m = first.AddMonths(n);
            var inMonth = billedLines.Where(l => l.IssueDate.Year == m.Year && l.IssueDate.Month == m.Month).ToList();
            var taxable = inMonth.Sum(l => l.Taxable);
            var tax = inMonth.Sum(l => l.Cgst + l.Sgst + l.Igst);
            return new MonthlyRevenue(m.Year, m.Month, inMonth.Select(l => l.InvoiceId).Distinct().Count(),
                taxable, tax, taxable + tax);
        }).ToList();

        var openLines = await db.Invoices.AsNoTracking()
            .Where(i => i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid)
            .SelectMany(i => i.Lines, (i, l) => new OpenLine(
                i.Id, i.DueDate, i.AmountPaid, l.TaxableValue + l.CgstAmount + l.SgstAmount + l.IgstAmount))
            .ToListAsync(cancellationToken);
        var open = openLines
            .GroupBy(l => l.InvoiceId)
            .Select(g => (g.First().DueDate, Balance: g.Sum(l => l.LineTotal) - g.First().AmountPaid))
            .ToList();
        var overdue = open.Where(i => i.DueDate < today).ToList();
        var drafts = await db.Invoices.CountAsync(i => i.Status == InvoiceStatus.Draft, cancellationToken);

        return new DashboardSummary(
            series,
            billedLines.Sum(l => l.Cgst), billedLines.Sum(l => l.Sgst), billedLines.Sum(l => l.Igst),
            overdue.Count, overdue.Sum(i => i.Balance), open.Sum(i => i.Balance), drafts);
    }
}

internal sealed record BilledLine(Guid InvoiceId, DateOnly IssueDate, decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst);

internal sealed record OpenLine(Guid InvoiceId, DateOnly DueDate, decimal AmountPaid, decimal LineTotal);
