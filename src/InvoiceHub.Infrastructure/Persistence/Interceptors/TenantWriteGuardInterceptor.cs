using InvoiceHub.Application.Abstractions;
using InvoiceHub.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace InvoiceHub.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Query filters protect reads. This protects writes: tenant-owned entities may only be
/// added or changed for the current tenant.
/// </summary>
public sealed class TenantWriteGuardInterceptor(ITenantProvider tenantProvider) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Check(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Check(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Check(DbContext? context)
    {
        if (context is null)
            return;

        var currentTenantId = tenantProvider.TenantId;
        foreach (var entry in context.ChangeTracker.Entries<ITenantOwned>())
        {
            // Deletes are checked too: removing another tenant's row is as bad as editing it.
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            if (currentTenantId is null || entry.Entity.TenantId != currentTenantId)
                throw new DomainException(
                    $"{entry.Metadata.ClrType.Name} belongs to a different tenant than the current one.");
        }
    }
}
