using InvoiceHub.Domain.Customers;
using InvoiceHub.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvoiceHub.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Name).HasMaxLength(Customer.NameMaxLength).IsRequired();
        builder.Property(c => c.Email).HasMaxLength(Customer.EmailMaxLength).IsRequired();
        builder.Property(c => c.Gstin).HasColumnType("char(15)");
        builder.Property(c => c.StateCode).HasColumnType("char(2)").IsRequired();
        builder.Ignore(c => c.IsRegistered);

        builder.OwnsOne(c => c.BillingAddress, address =>
        {
            address.Property(a => a.Line1).HasColumnName("BillingLine1").HasMaxLength(200).IsRequired();
            address.Property(a => a.Line2).HasColumnName("BillingLine2").HasMaxLength(200);
            address.Property(a => a.City).HasColumnName("BillingCity").HasMaxLength(100).IsRequired();
            address.Property(a => a.PostalCode).HasColumnName("BillingPostalCode").HasMaxLength(10).IsRequired();
        });
        builder.Navigation(c => c.BillingAddress).IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(c => c.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.TenantId, c.Email }).IsUnique();
    }
}
