using InvoiceHub.Domain.Common;

namespace InvoiceHub.Domain.Tenants;

/// <summary>A business that issues invoices. Every other aggregate belongs to one tenant.</summary>
public sealed class Tenant : AuditableEntity
{
    public const int NameMaxLength = 200;

    private Tenant()
    {
        Name = null!;
        Gstin = null!;
        StateCode = null!;
    }

    public Tenant(string name, string gstin, string stateCode)
    {
        Name = Guard.NotBlank(name, "Tenant name", NameMaxLength);
        StateCode = GstRules.StateCode(stateCode);
        Gstin = GstRules.Gstin(gstin, StateCode);
        IsActive = true;
    }

    public string Name { get; private set; }
    public string Gstin { get; private set; }
    public string StateCode { get; private set; }
    public bool IsActive { get; private set; }

    public void Rename(string name) => Name = Guard.NotBlank(name, "Tenant name", NameMaxLength);

    public void Deactivate() => IsActive = false;
}
