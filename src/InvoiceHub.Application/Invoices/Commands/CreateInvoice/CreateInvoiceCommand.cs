using InvoiceHub.Application.Abstractions;
using InvoiceHub.Application.Common;
using InvoiceHub.Domain.Customers;
using InvoiceHub.Domain.Invoices;
using InvoiceHub.Domain.Products;
using InvoiceHub.Domain.Tenants;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Application.Invoices.Commands.CreateInvoice;

/// <summary>
/// One line of a new invoice. Either a catalogue line (ProductId plus Quantity; price, HSN code and GST rate
/// come from the product) or a custom line (Description, HsnCode, UnitPrice and GstRate, with no ProductId).
/// </summary>
public sealed record CreateInvoiceLine(
    Guid? ProductId,
    string? Description,
    string? HsnCode,
    decimal Quantity,
    decimal? UnitPrice,
    decimal? GstRate);

public sealed record CreateInvoiceCommand(
    Guid CustomerId,
    string InvoiceNumber,
    DateOnly IssueDate,
    DateOnly DueDate,
    string? Notes,
    IReadOnlyList<CreateInvoiceLine> Lines) : IRequest<InvoiceDto>;

public sealed class CreateInvoiceCommandHandler(IApplicationDbContext db, ITenantProvider tenantProvider, IBusinessClock clock)
    : IRequestHandler<CreateInvoiceCommand, InvoiceDto>
{
    public async Task<InvoiceDto> Handle(CreateInvoiceCommand request, CancellationToken cancellationToken)
    {
        var tenantId = tenantProvider.TenantId
            ?? throw new InvalidOperationException("Creating an invoice requires a current tenant.");

        // Both lookups go through the tenant query filter, so another tenant's customer or product is NotFound.
        // Tenant is not a tenant-owned row, so look it up by the caller's own id.
        var sellerStateCode = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => t.StateCode)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(Tenant), tenantId);

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken)
            ?? throw new NotFoundException(nameof(Customer), request.CustomerId);

        var productIds = request.Lines.Where(l => l.ProductId.HasValue).Select(l => l.ProductId!.Value).Distinct().ToList();
        var products = await db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var missingProduct = productIds.FirstOrDefault(id => !products.ContainsKey(id));
        if (missingProduct != Guid.Empty)
            throw new NotFoundException(nameof(Product), missingProduct);

        var invoiceNumber = request.InvoiceNumber.Trim();
        if (await db.Invoices.AnyAsync(i => i.InvoiceNumber == invoiceNumber, cancellationToken))
            throw new ConflictException($"Invoice number '{invoiceNumber}' is already used.");

        var invoice = new Invoice(tenantId, sellerStateCode, customer, invoiceNumber, request.IssueDate, request.DueDate);
        invoice.SetNotes(request.Notes);

        foreach (var line in request.Lines)
        {
            if (line.ProductId is { } productId)
                invoice.AddLine(products[productId], line.Quantity);
            else
                invoice.AddCustomLine(line.Description!, line.HsnCode!, line.Quantity, line.UnitPrice!.Value, line.GstRate!.Value);
        }

        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken);

        return invoice.ToDto(customer.Name, clock.Today());
    }
}
