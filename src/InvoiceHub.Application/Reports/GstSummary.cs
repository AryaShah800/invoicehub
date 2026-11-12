using FluentValidation;
using InvoiceHub.Application.Abstractions;
using MediatR;

namespace InvoiceHub.Application.Reports;

/// <summary>One GST slab in the report. Amounts are sums of the already-rounded line amounts.</summary>
public sealed record GstSummaryRow(
    decimal GstRate,
    int InvoiceCount,
    decimal TaxableValue,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal IgstAmount)
{
    public decimal TotalTax => CgstAmount + SgstAmount + IgstAmount;
}

public sealed record GstSummaryReport(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<GstSummaryRow> Rows,
    decimal TaxableValue,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal IgstAmount,
    decimal TotalTax);

/// <summary>
/// Reads the GST summary for one tenant. Implementations must filter by the given tenant themselves
/// when they bypass EF Core query filters (as the Dapper one does).
/// </summary>
public interface IGstReportReader
{
    Task<IReadOnlyList<GstSummaryRow>> GetSummaryAsync(Guid tenantId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

/// <summary>
/// GST summary by slab for invoices issued in a date range. Draft and cancelled invoices are left out.
/// </summary>
public sealed record GetGstSummaryQuery(DateOnly From, DateOnly To) : IRequest<GstSummaryReport>;

public sealed class GetGstSummaryQueryValidator : AbstractValidator<GetGstSummaryQuery>
{
    public GetGstSummaryQueryValidator()
    {
        RuleFor(q => q.From).NotEqual(default(DateOnly));
        RuleFor(q => q.To).NotEqual(default(DateOnly)).GreaterThanOrEqualTo(q => q.From)
            .WithMessage("'To' must not be before 'From'.");
    }
}

public sealed class GetGstSummaryQueryHandler(IGstReportReader reader, ITenantProvider tenantProvider)
    : IRequestHandler<GetGstSummaryQuery, GstSummaryReport>
{
    public async Task<GstSummaryReport> Handle(GetGstSummaryQuery request, CancellationToken cancellationToken)
    {
        var tenantId = tenantProvider.TenantId
            ?? throw new InvalidOperationException("The GST report requires a current tenant.");

        var rows = await reader.GetSummaryAsync(tenantId, request.From, request.To, cancellationToken);

        return new GstSummaryReport(
            request.From, request.To, rows,
            rows.Sum(r => r.TaxableValue),
            rows.Sum(r => r.CgstAmount),
            rows.Sum(r => r.SgstAmount),
            rows.Sum(r => r.IgstAmount),
            rows.Sum(r => r.TotalTax));
    }
}
