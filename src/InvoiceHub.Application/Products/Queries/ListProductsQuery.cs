using FluentValidation;
using InvoiceHub.Application.Abstractions;
using InvoiceHub.Application.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Application.Products.Queries;

public sealed record ListProductsQuery(string? Search = null, bool? IsActive = null, int Page = 1, int PageSize = PagedResult<ProductDto>.DefaultPageSize)
    : IRequest<PagedResult<ProductDto>>;

public sealed class ListProductsQueryValidator : AbstractValidator<ListProductsQuery>
{
    public ListProductsQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PagedResult<ProductDto>.MaxPageSize);
    }
}

public sealed class ListProductsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ListProductsQuery, PagedResult<ProductDto>>
{
    public async Task<PagedResult<ProductDto>> Handle(ListProductsQuery request, CancellationToken cancellationToken)
    {
        var query = db.Products.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(p => p.Name.Contains(term) || p.HsnCode.Contains(term));
        }
        if (request.IsActive is { } active)
            query = query.Where(p => p.IsActive == active);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<ProductDto>(items.Select(p => p.ToDto()).ToList(), request.Page, request.PageSize, total);
    }
}
