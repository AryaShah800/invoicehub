using InvoiceHub.Domain.Common;

namespace InvoiceHub.Domain.Products;

/// <summary>A good or service in the tenant catalogue, with its HSN/SAC code and GST rate.</summary>
public sealed class Product : AuditableEntity, ITenantOwned
{
    public const int NameMaxLength = 200;

    private Product()
    {
        Name = null!;
        HsnCode = null!;
    }

    public Product(Guid tenantId, string name, string hsnCode, decimal unitPrice, decimal gstRate)
    {
        TenantId = Guard.NotEmpty(tenantId, "Tenant");
        Name = Guard.NotBlank(name, "Product name", NameMaxLength);
        HsnCode = GstRules.HsnCode(hsnCode);
        UnitPrice = Guard.NotNegative(unitPrice, "Unit price");
        GstRate = GstRules.Rate(gstRate);
        IsActive = true;
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string HsnCode { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal GstRate { get; private set; }
    public bool IsActive { get; private set; }

    public void ChangePrice(decimal unitPrice) => UnitPrice = Guard.NotNegative(unitPrice, "Unit price");

    public void ChangeGstRate(decimal gstRate) => GstRate = GstRules.Rate(gstRate);

    public void Deactivate() => IsActive = false;
}
