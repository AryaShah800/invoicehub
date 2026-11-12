using InvoiceHub.Domain.Common;

namespace InvoiceHub.Domain.Invoices;

/// <summary>
/// One line on an invoice. Product details are copied onto the line so later catalogue
/// changes do not alter invoices that were already raised.
/// </summary>
public sealed class InvoiceLine : Entity, ITenantOwned
{
    public const int DescriptionMaxLength = 500;

    private InvoiceLine()
    {
        Description = null!;
        HsnCode = null!;
    }

    internal InvoiceLine(Guid tenantId, Guid invoiceId, int lineNumber, Guid? productId, string description, string hsnCode,
        decimal quantity, decimal unitPrice, decimal gstRate)
    {
        TenantId = tenantId;
        InvoiceId = invoiceId;
        LineNumber = lineNumber;
        ProductId = productId;
        Description = Guard.NotBlank(description, "Description", DescriptionMaxLength);
        HsnCode = GstRules.HsnCode(hsnCode);
        Quantity = Guard.Positive(quantity, "Quantity");
        UnitPrice = Guard.NotNegative(unitPrice, "Unit price");
        GstRate = GstRules.Rate(gstRate);
        TaxableValue = Math.Round(Quantity * UnitPrice, 2, MidpointRounding.AwayFromZero);
    }

    public Guid TenantId { get; private set; }
    public Guid InvoiceId { get; private set; }

    /// <summary>Position on the invoice, starting at 1.</summary>
    public int LineNumber { get; private set; }

    public Guid? ProductId { get; private set; }
    public string Description { get; private set; }
    public string HsnCode { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal GstRate { get; private set; }
    public decimal TaxableValue { get; private set; }

    internal void Renumber(int lineNumber) => LineNumber = lineNumber;
}
