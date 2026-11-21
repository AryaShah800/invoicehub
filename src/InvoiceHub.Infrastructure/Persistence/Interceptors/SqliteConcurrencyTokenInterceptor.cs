using InvoiceHub.Domain.Invoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace InvoiceHub.Infrastructure.Persistence.Interceptors;

/// <summary>
/// SQLite only. SQL Server generates the RowVersion column itself; SQLite cannot, so the application gives every
/// new or changed Invoice and RecurringInvoice a fresh random token on save. EF includes the old token in the
/// UPDATE's WHERE clause, so a concurrent writer that saw the old token fails with DbUpdateConcurrencyException,
/// exactly as on SQL Server.
/// </summary>
public sealed class SqliteConcurrencyTokenInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Refresh(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Refresh(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Refresh(DbContext? context)
    {
        if (context is null)
            return;

        context.ChangeTracker.DetectChanges();
        foreach (var entry in context.ChangeTracker.Entries().Where(IsVersioned))
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Property(nameof(Invoice.RowVersion)).CurrentValue = Guid.NewGuid().ToByteArray();
        }
    }

    private static bool IsVersioned(EntityEntry entry) => entry.Entity is Invoice or RecurringInvoice;
}
