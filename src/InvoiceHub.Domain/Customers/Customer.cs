using InvoiceHub.Domain.Common;

namespace InvoiceHub.Domain.Customers;

/// <summary>
/// A buyer. Registered businesses (B2B) have a GSTIN; consumers (B2C) do not.
/// The state code decides the place of supply on invoices.
/// </summary>
public sealed class Customer : AuditableEntity, ITenantOwned
{
    public const int NameMaxLength = 200;
    public const int EmailMaxLength = 256;

    private Customer()
    {
        Name = null!;
        Email = null!;
        StateCode = null!;
        BillingAddress = null!;
    }

    public Customer(Guid tenantId, string name, string email, string stateCode, Address billingAddress, string? gstin = null)
    {
        TenantId = Guard.NotEmpty(tenantId, "Tenant");
        Name = Guard.NotBlank(name, "Customer name", NameMaxLength);
        Email = Guard.NotBlank(email, "Email", EmailMaxLength);
        StateCode = GstRules.StateCode(stateCode);
        BillingAddress = billingAddress ?? throw new DomainException("Billing address is required.");
        Gstin = string.IsNullOrWhiteSpace(gstin) ? null : GstRules.Gstin(gstin, StateCode);
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string Email { get; private set; }
    public string? Gstin { get; private set; }
    public string StateCode { get; private set; }
    public Address BillingAddress { get; private set; }

    public bool IsRegistered => Gstin is not null;

    public void UpdateContact(string name, string email)
    {
        Name = Guard.NotBlank(name, "Customer name", NameMaxLength);
        Email = Guard.NotBlank(email, "Email", EmailMaxLength);
    }

    public void ChangeBillingAddress(Address address) =>
        BillingAddress = address ?? throw new DomainException("Billing address is required.");
}
