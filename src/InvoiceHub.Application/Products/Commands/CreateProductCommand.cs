using FluentValidation;
using InvoiceHub.Application.Abstractions;
using InvoiceHub.Domain.Common;
using InvoiceHub.Domain.Products;
using MediatR;

namespace InvoiceHub.Application.Products.Commands;

public sealed record CreateProductCommand(string Name, string HsnCode, decimal UnitPrice, decimal GstRate) : IRequest<ProductDto>;

public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(p => p.Name).NotEmpty().MaximumLength(Product.NameMaxLength);
        RuleFor(p => p.HsnCode).NotEmpty().Matches(@"^(\d{4}|\d{6}|\d{8})$").WithMessage("'Hsn Code' must be 4, 6 or 8 digits.");
        RuleFor(p => p.UnitPrice).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, ignoreTrailingZeros: true);
        RuleFor(p => p.GstRate).Must(GstRules.ValidRates.Contains).WithMessage("'Gst Rate' must be a GST slab: 0, 0.25, 3, 5, 12, 18 or 28.");
    }
}

public sealed class CreateProductCommandHandler(IApplicationDbContext db, ITenantProvider tenantProvider)
    : IRequestHandler<CreateProductCommand, ProductDto>
{
    public async Task<ProductDto> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        var tenantId = tenantProvider.TenantId
            ?? throw new InvalidOperationException("Creating a product requires a current tenant.");

        var product = new Product(tenantId, request.Name.Trim(), request.HsnCode, request.UnitPrice, request.GstRate);
        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);
        return product.ToDto();
    }
}
