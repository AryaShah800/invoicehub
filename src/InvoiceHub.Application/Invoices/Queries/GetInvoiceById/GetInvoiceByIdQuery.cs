using FluentValidation;
using InvoiceHub.Application.Abstractions;
using InvoiceHub.Application.Common;
using InvoiceHub.Domain.Invoices;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Application.Invoices.Queries.GetInvoiceById;

public sealed record GetInvoiceByIdQuery(Guid Id) : IRequest<InvoiceDto>;

public sealed class GetInvoiceByIdQueryValidator : AbstractValidator<GetInvoiceByIdQuery>
{
    public GetInvoiceByIdQueryValidator() => RuleFor(q => q.Id).NotEmpty();
}

public sealed class GetInvoiceByIdQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetInvoiceByIdQuery, InvoiceDto>
{
    public async Task<InvoiceDto> Handle(GetInvoiceByIdQuery request, CancellationToken cancellationToken)
    {
        // The tenant query filter hides other tenants' invoices, so they look the same as missing ones.
        var invoice = await db.Invoices
            .AsNoTracking()
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Invoice), request.Id);

        var customerName = await db.Customers
            .AsNoTracking()
            .Where(c => c.Id == invoice.CustomerId)
            .Select(c => c.Name)
            .FirstAsync(cancellationToken);

        return invoice.ToDto(customerName);
    }
}
