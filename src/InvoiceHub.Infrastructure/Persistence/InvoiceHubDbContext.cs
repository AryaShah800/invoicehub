using InvoiceHub.Application.Abstractions;
using InvoiceHub.Domain.Customers;
using InvoiceHub.Domain.Invoices;
using InvoiceHub.Domain.Products;
using InvoiceHub.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Infrastructure.Persistence;

public sealed class InvoiceHubDbContext(
    DbContextOptions<InvoiceHubDbContext> options,
    ITenantProvider tenantProvider) : DbContext(options)
{
    /// <summary>The tenant of the current request; null when none is known.</summary>
    public Guid? CurrentTenantId => tenantProvider.TenantId;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InvoiceHubDbContext).Assembly);
    }
}
