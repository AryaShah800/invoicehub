using System.Linq.Expressions;
using InvoiceHub.Application.Abstractions;
using InvoiceHub.Domain.Common;
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
        ApplyTenantFilters(modelBuilder);
    }

    /// <summary>
    /// Adds a global query filter "TenantId == CurrentTenantId" to every tenant-owned entity.
    /// With no current tenant the comparison matches nothing, so access fails closed.
    /// </summary>
    private void ApplyTenantFilters(ModelBuilder modelBuilder)
    {
        var tenantOwnedTypes = modelBuilder.Model.GetEntityTypes()
            .Select(t => t.ClrType)
            .Where(t => typeof(ITenantOwned).IsAssignableFrom(t));

        foreach (var clrType in tenantOwnedTypes)
        {
            var entity = Expression.Parameter(clrType, "e");
            var entityTenantId = Expression.Convert(
                Expression.Property(entity, nameof(ITenantOwned.TenantId)), typeof(Guid?));
            var currentTenantId = Expression.Property(Expression.Constant(this), nameof(CurrentTenantId));

            var filter = Expression.Lambda(Expression.Equal(entityTenantId, currentTenantId), entity);
            modelBuilder.Entity(clrType).HasQueryFilter(filter);
        }
    }
}
