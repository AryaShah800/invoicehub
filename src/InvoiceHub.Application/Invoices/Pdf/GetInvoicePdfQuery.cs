using InvoiceHub.Application.Abstractions;
using InvoiceHub.Application.Common;
using InvoiceHub.Domain.Invoices;
using InvoiceHub.Domain.Tenants;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Application.Invoices.Pdf;

public sealed record InvoicePdf(string FileName, byte[] Content);

public sealed record GetInvoicePdfQuery(Guid Id) : IRequest<InvoicePdf>;

public sealed class GetInvoicePdfQueryHandler(IApplicationDbContext db, ITenantProvider tenantProvider, IInvoicePdfRenderer renderer)
    : IRequestHandler<GetInvoicePdfQuery, InvoicePdf>
{
    public async Task<InvoicePdf> Handle(GetInvoicePdfQuery request, CancellationToken cancellationToken)
    {
        var tenantId = tenantProvider.TenantId
            ?? throw new InvalidOperationException("Rendering an invoice requires a current tenant.");

        // The tenant query filter makes another tenant's invoice a 404, same as a missing one.
        var invoice = await db.Invoices.AsNoTracking().Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Invoice), request.Id);

        var customer = await db.Customers.AsNoTracking().FirstAsync(c => c.Id == invoice.CustomerId, cancellationToken);
        var seller = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken)
            ?? throw new NotFoundException(nameof(Tenant), tenantId);

        var address = customer.BillingAddress;
        var addressText = string.Join(", ", new[] { address.Line1, address.Line2, address.City, address.PostalCode }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

        var model = new InvoicePdfModel(invoice.ToDto(customer.Name), seller.Name, seller.Gstin,
            customer.Gstin ?? "Unregistered", addressText, customer.StateCode);

        var safeNumber = string.Concat(invoice.InvoiceNumber.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));
        return new InvoicePdf($"invoice-{safeNumber}.pdf", renderer.Render(model));
    }
}
