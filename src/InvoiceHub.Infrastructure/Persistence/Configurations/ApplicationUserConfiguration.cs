using InvoiceHub.Application.Auth;
using InvoiceHub.Domain.Tenants;
using InvoiceHub.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvoiceHub.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(u => u.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(u => u.TenantId);
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<IdentityRole<Guid>>
{
    // Fixed ids and stamps keep the seed data stable across migrations.
    public static readonly Guid AdminRoleId = new("5b2f7c0e-7a43-4a0e-9c1a-1d0b6c9e0001");
    public static readonly Guid StaffRoleId = new("5b2f7c0e-7a43-4a0e-9c1a-1d0b6c9e0002");

    public void Configure(EntityTypeBuilder<IdentityRole<Guid>> builder)
    {
        builder.HasData(
            new IdentityRole<Guid>
            {
                Id = AdminRoleId,
                Name = Roles.Admin,
                NormalizedName = Roles.Admin.ToUpperInvariant(),
                ConcurrencyStamp = "admin-role-stamp-v1",
            },
            new IdentityRole<Guid>
            {
                Id = StaffRoleId,
                Name = Roles.Staff,
                NormalizedName = Roles.Staff.ToUpperInvariant(),
                ConcurrencyStamp = "staff-role-stamp-v1",
            });
    }
}
