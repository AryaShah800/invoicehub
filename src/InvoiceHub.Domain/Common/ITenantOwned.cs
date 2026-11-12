namespace InvoiceHub.Domain.Common;

/// <summary>
/// Marks an entity that belongs to a single tenant. Used later for EF Core global query filters.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}
