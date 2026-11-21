using FluentValidation;
using InvoiceHub.Application.Abstractions;
using InvoiceHub.Application.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Application.Customers.Queries;

public sealed record ListCustomersQuery(string? Search = null, int Page = 1, int PageSize = PagedResult<CustomerDto>.DefaultPageSize)
    : IRequest<PagedResult<CustomerDto>>;

public sealed class ListCustomersQueryValidator : AbstractValidator<ListCustomersQuery>
{
    public ListCustomersQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PagedResult<CustomerDto>.MaxPageSize);
    }
}

public sealed class ListCustomersQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ListCustomersQuery, PagedResult<CustomerDto>>
{
    public async Task<PagedResult<CustomerDto>> Handle(ListCustomersQuery request, CancellationToken cancellationToken)
    {
        // The tenant query filter applies here, so only the caller's customers can be listed.
        var query = db.Customers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(c => c.Name.Contains(term) || c.Email.Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<CustomerDto>(items.Select(c => c.ToDto()).ToList(), request.Page, request.PageSize, total);
    }
}
