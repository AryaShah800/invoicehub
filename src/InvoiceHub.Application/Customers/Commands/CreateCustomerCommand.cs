using FluentValidation;
using InvoiceHub.Application.Abstractions;
using InvoiceHub.Domain.Customers;
using MediatR;

namespace InvoiceHub.Application.Customers.Commands;

public sealed record CreateCustomerCommand(
    string Name,
    string Email,
    string? Gstin,
    string StateCode,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string PostalCode) : IRequest<CustomerDto>;

public sealed class CreateCustomerCommandValidator : AbstractValidator<CreateCustomerCommand>
{
    public CreateCustomerCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(Customer.NameMaxLength);
        RuleFor(c => c.Email).NotEmpty().MaximumLength(Customer.EmailMaxLength).EmailAddress();
        RuleFor(c => c.StateCode).NotEmpty().Matches(@"^\d{2}$").WithMessage("'State Code' must be two digits.");
        RuleFor(c => c.AddressLine1).NotEmpty().MaximumLength(200);
        RuleFor(c => c.AddressLine2).MaximumLength(200);
        RuleFor(c => c.City).NotEmpty().MaximumLength(100);
        RuleFor(c => c.PostalCode).NotEmpty().MaximumLength(10);
        RuleFor(c => c.Gstin).MaximumLength(15);
    }
}

public sealed class CreateCustomerCommandHandler(IApplicationDbContext db, ITenantProvider tenantProvider)
    : IRequestHandler<CreateCustomerCommand, CustomerDto>
{
    public async Task<CustomerDto> Handle(CreateCustomerCommand request, CancellationToken cancellationToken)
    {
        var tenantId = tenantProvider.TenantId
            ?? throw new InvalidOperationException("Creating a customer requires a current tenant.");

        // The entity checks the GSTIN format and that its first two digits match the state code.
        var customer = new Customer(
            tenantId, request.Name.Trim(), request.Email.Trim(), request.StateCode,
            new Address(request.AddressLine1, request.AddressLine2, request.City, request.PostalCode),
            request.Gstin);

        db.Customers.Add(customer);
        await db.SaveChangesAsync(cancellationToken);
        return customer.ToDto();
    }
}
