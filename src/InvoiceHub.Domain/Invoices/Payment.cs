using InvoiceHub.Domain.Common;

namespace InvoiceHub.Domain.Invoices;

/// <summary>
/// A payment received against an invoice, normally reported by the payment provider webhook.
/// The provider event id is stored so a repeated delivery of the same event is not applied twice.
/// </summary>
public sealed class Payment : AuditableEntity, ITenantOwned
{
    public const int EventIdMaxLength = 100;
    public const int ReferenceMaxLength = 100;

    private Payment()
    {
        EventId = null!;
    }

    internal Payment(Guid tenantId, Guid invoiceId, string eventId, decimal amount, string? reference, DateTime receivedAtUtc)
    {
        TenantId = Guard.NotEmpty(tenantId, "Tenant");
        InvoiceId = Guard.NotEmpty(invoiceId, "Invoice");
        EventId = Guard.NotBlank(eventId, "Event id", EventIdMaxLength);
        Amount = amount;
        Reference = string.IsNullOrWhiteSpace(reference) ? null : Guard.NotBlank(reference, "Reference", ReferenceMaxLength);
        ReceivedAtUtc = receivedAtUtc;
    }

    public Guid TenantId { get; private set; }
    public Guid InvoiceId { get; private set; }

    /// <summary>The provider id for the payment event. Unique per tenant.</summary>
    public string EventId { get; private set; }

    /// <summary>Amount in rupees.</summary>
    public decimal Amount { get; private set; }

    public string? Reference { get; private set; }
    public DateTime ReceivedAtUtc { get; private set; }
}
