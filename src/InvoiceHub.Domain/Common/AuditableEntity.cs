namespace InvoiceHub.Domain.Common;

/// <summary>
/// Entity with creation and modification timestamps. The timestamps are set by the
/// persistence layer when changes are saved, not by domain code.
/// </summary>
public abstract class AuditableEntity : Entity
{
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public void MarkCreated(DateTime utcNow) => CreatedAtUtc = utcNow;
    public void MarkUpdated(DateTime utcNow) => UpdatedAtUtc = utcNow;
}
