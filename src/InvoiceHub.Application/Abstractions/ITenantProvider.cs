namespace InvoiceHub.Application.Abstractions;

/// <summary>
/// Supplies the tenant of the current request. Null means no tenant is known,
/// in which case tenant-owned data is not visible (fail closed).
/// </summary>
public interface ITenantProvider
{
    Guid? TenantId { get; }
}
