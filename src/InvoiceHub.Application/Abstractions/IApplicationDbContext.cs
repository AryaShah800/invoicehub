using InvoiceHub.Domain.Customers;
using InvoiceHub.Domain.Invoices;
using InvoiceHub.Domain.Products;
using InvoiceHub.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Application.Abstractions;

/// <summary>
/// What handlers need from the database. Implemented by the Infrastructure DbContext, which applies
/// the tenant query filters, so handlers never see another tenant's rows.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Customer> Customers { get; }
    DbSet<Product> Products { get; }
    DbSet<Invoice> Invoices { get; }
    DbSet<Tenant> Tenants { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
