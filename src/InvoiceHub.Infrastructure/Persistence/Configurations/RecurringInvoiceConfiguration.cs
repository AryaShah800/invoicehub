using InvoiceHub.Domain.Customers;
using InvoiceHub.Domain.Invoices;
using InvoiceHub.Domain.Products;
using InvoiceHub.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvoiceHub.Infrastructure.Persistence.Configurations;

internal sealed class RecurringInvoiceConfiguration : IEntityTypeConfiguration<RecurringInvoice>
{
    public void Configure(EntityTypeBuilder<RecurringInvoice> builder)
    {
        builder.ToTable("RecurringInvoices");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Name).HasMaxLength(RecurringInvoice.NameMaxLength).IsRequired();
        builder.Property(r => r.RowVersion).IsRowVersion();

        builder.HasMany(r => r.Lines)
            .WithOne()
            .HasForeignKey(l => l.RecurringInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Customer>().WithMany().HasForeignKey(r => r.CustomerId).OnDelete(DeleteBehavior.Restrict);

        // The job looks for active templates whose next run is due.
        builder.HasIndex(r => new { r.TenantId, r.IsActive, r.NextRunDate });
    }
}

internal sealed class RecurringInvoiceLineConfiguration : IEntityTypeConfiguration<RecurringInvoiceLine>
{
    public void Configure(EntityTypeBuilder<RecurringInvoiceLine> builder)
    {
        builder.ToTable("RecurringInvoiceLines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Quantity).HasPrecision(18, 3);
        builder.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}
