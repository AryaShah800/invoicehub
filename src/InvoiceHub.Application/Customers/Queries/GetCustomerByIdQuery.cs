using FluentValidation;
using InvoiceHub.Application.Abstractions;
using InvoiceHub.Application.Common;
using InvoiceHub.Domain.Customers;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Application.Customers.Queries;

public sealed record GetCustomerByIdQuery(Guid Id) : IRequest<CustomerDto>;

public sealed class GetCustomerByIdQueryValidator : AbstractValidator<GetCustomerByIdQuery>
{
    public GetCustomerByIdQueryValidator() => RuleFor(q => q.Id).NotEmpty();
}

public sealed class GetCustomerByIdQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetCustomerByIdQuery, CustomerDto>
{
    public async Task<CustomerDto> Handle(GetCustomerByIdQuery request, CancellationToken cancellationToken)
    {
        // The tenant query filter hides other tenants' customers, so they look the same as missing ones.
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Customer), request.Id);
        return customer.ToDto();
    }
}
