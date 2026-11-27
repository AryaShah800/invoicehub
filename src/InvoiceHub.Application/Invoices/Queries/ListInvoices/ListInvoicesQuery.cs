using FluentValidation;
using InvoiceHub.Application.Abstractions;
using InvoiceHub.Application.Common;
using InvoiceHub.Domain.Invoices;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Application.Invoices.Queries.ListInvoices;

public sealed record InvoiceSummaryDto(
    Guid Id,
    string InvoiceNumber,
    string Status,
    DateOnly IssueDate,
    DateOnly DueDate,
    Guid CustomerId,
    string CustomerName,
    decimal GrandTotal,
    decimal AmountPaid,
    decimal BalanceDue,
    bool IsOverdue);

/// <summary>
/// Filters: Status (enum name), CustomerId, issue date range (From/To, inclusive), and Overdue
/// (true = issued or partly paid and past the due date as of today).
/// </summary>
public sealed record ListInvoicesQuery(
    InvoiceStatus? Status = null,
    Guid? CustomerId = null,
    DateOnly? From = null,
    DateOnly? To = null,
    bool? Overdue = null,
    int Page = 1,
    int PageSize = PagedResult<InvoiceSummaryDto>.DefaultPageSize) : IRequest<PagedResult<InvoiceSummaryDto>>;

public sealed class ListInvoicesQueryValidator : AbstractValidator<ListInvoicesQuery>
{
    public ListInvoicesQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PagedResult<InvoiceSummaryDto>.MaxPageSize);
        RuleFor(q => q.To).GreaterThanOrEqualTo(q => q.From!.Value).When(q => q.From is not null && q.To is not null)
            .WithMessage("To must not be before From.");
    }
}

public sealed class ListInvoicesQueryHandler(IApplicationDbContext db, IBusinessClock clock)
    : IRequestHandler<ListInvoicesQuery, PagedResult<InvoiceSummaryDto>>
{
    public async Task<PagedResult<InvoiceSummaryDto>> Handle(ListInvoicesQuery request, CancellationToken cancellationToken)
    {
        var today = clock.Today();
        var issued = InvoiceStatus.Issued;
        var partly = InvoiceStatus.PartiallyPaid;

        // The tenant query filter applies to invoices and customers alike.
        var query = db.Invoices.AsNoTracking();
        if (request.Status is { } status) query = query.Where(i => i.Status == status);
        if (request.CustomerId is { } customerId) query = query.Where(i => i.CustomerId == customerId);
        if (request.From is { } from) query = query.Where(i => i.IssueDate >= from);
        if (request.To is { } to) query = query.Where(i => i.IssueDate <= to);
        if (request.Overdue is { } overdue)
        {
            query = overdue
                ? query.Where(i => (i.Status == issued || i.Status == partly) && i.DueDate < today)
                : query.Where(i => !((i.Status == issued || i.Status == partly) && i.DueDate < today));
        }

        var total = await query.CountAsync(cancellationToken);
        var page = await query
            .Include(i => i.Lines)
            .OrderByDescending(i => i.IssueDate).ThenByDescending(i => i.InvoiceNumber)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var ids = page.Select(i => i.CustomerId).Distinct().ToList();
        var names = await db.Customers.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        var items = page.Select(i => new InvoiceSummaryDto(
            i.Id, i.InvoiceNumber, i.Status.ToString(), i.IssueDate, i.DueDate, i.CustomerId,
            names.GetValueOrDefault(i.CustomerId, ""), i.GrandTotal, i.AmountPaid, i.BalanceDue,
            (i.Status == issued || i.Status == partly) && i.DueDate < today)).ToList();
        return new PagedResult<InvoiceSummaryDto>(items, request.Page, request.PageSize, total);
    }
}
