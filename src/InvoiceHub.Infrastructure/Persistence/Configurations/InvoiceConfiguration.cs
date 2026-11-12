using InvoiceHub.Domain.Customers;
using InvoiceHub.Domain.Invoices;
using InvoiceHub.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvoiceHub.Infrastructure.Persistence.Configurations;

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.InvoiceNumber).HasColumnType("varchar(16)").IsRequired();
        builder.Property(i => i.SellerStateCode).HasColumnType("char(2)").IsRequired();
        builder.Property(i => i.PlaceOfSupplyStateCode).HasColumnType("char(2)").IsRequired();
        builder.Property(i => i.Notes).HasMaxLength(Invoice.NotesMaxLength);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.RowVersion).IsRowVersion();
        builder.Ignore(i => i.TaxableTotal);
        builder.Ignore(i => i.CgstTotal);
        builder.Ignore(i => i.SgstTotal);
        builder.Ignore(i => i.IgstTotal);
        builder.Ignore(i => i.TaxTotal);
        builder.Ignore(i => i.GrandTotal);
        builder.Ignore(i => i.IsIntraState);

        builder.HasMany(i => i.Lines)
            .WithOne()
            .HasForeignKey(l => l.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(i => i.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(i => i.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(i => i.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => new { i.TenantId, i.InvoiceNumber }).IsUnique();
        builder.HasIndex(i => new { i.TenantId, i.Status, i.DueDate });
        // Serves the GST report: one tenant, a date range, then a status filter without touching the table.
        builder.HasIndex(i => new { i.TenantId, i.IssueDate }).IncludeProperties(i => i.Status);
    }
}
