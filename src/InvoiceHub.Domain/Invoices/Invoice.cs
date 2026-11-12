using InvoiceHub.Domain.Common;
using InvoiceHub.Domain.Customers;
using InvoiceHub.Domain.Products;

namespace InvoiceHub.Domain.Invoices;

/// <summary>
/// Invoice aggregate root. Lines can only change while the invoice is a draft.
/// </summary>
public sealed class Invoice : AuditableEntity, ITenantOwned
{
    public const int NumberMaxLength = 16;
    public const int NotesMaxLength = 1000;

    private readonly List<InvoiceLine> _lines = [];

    private Invoice()
    {
        InvoiceNumber = null!;
        PlaceOfSupplyStateCode = null!;
    }

    public Invoice(Guid tenantId, Customer customer, string invoiceNumber, DateOnly issueDate, DateOnly dueDate)
    {
        TenantId = Guard.NotEmpty(tenantId, "Tenant");
        if (customer is null)
            throw new DomainException("Customer is required.");
        if (customer.TenantId != tenantId)
            throw new DomainException("Customer belongs to a different tenant.");
        if (dueDate < issueDate)
            throw new DomainException("Due date cannot be before the issue date.");

        CustomerId = customer.Id;
        InvoiceNumber = Guard.NotBlank(invoiceNumber, "Invoice number", NumberMaxLength);
        IssueDate = issueDate;
        DueDate = dueDate;
        PlaceOfSupplyStateCode = customer.StateCode;
        Status = InvoiceStatus.Draft;
    }

    public Guid TenantId { get; private set; }
    public Guid CustomerId { get; private set; }
    public string InvoiceNumber { get; private set; }
    public DateOnly IssueDate { get; private set; }
    public DateOnly DueDate { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public string PlaceOfSupplyStateCode { get; private set; }
    public string? Notes { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<InvoiceLine> Lines => _lines.AsReadOnly();

    public decimal TaxableTotal => _lines.Sum(l => l.TaxableValue);

    public InvoiceLine AddLine(Product product, decimal quantity)
    {
        if (product is null)
            throw new DomainException("Product is required.");
        if (product.TenantId != TenantId)
            throw new DomainException("Product belongs to a different tenant.");
        if (!product.IsActive)
            throw new DomainException("Product is inactive.");

        return AddLine(product.Id, product.Name, product.HsnCode, quantity, product.UnitPrice, product.GstRate);
    }

    public InvoiceLine AddCustomLine(string description, string hsnCode, decimal quantity, decimal unitPrice, decimal gstRate) =>
        AddLine(null, description, hsnCode, quantity, unitPrice, gstRate);

    public void RemoveLine(Guid lineId)
    {
        EnsureDraft();
        var line = _lines.Find(l => l.Id == lineId)
            ?? throw new DomainException("Invoice line not found.");
        _lines.Remove(line);
    }

    public void SetNotes(string? notes)
    {
        EnsureDraft();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : Guard.NotBlank(notes, "Notes", NotesMaxLength);
    }

    public void Issue()
    {
        EnsureDraft();
        if (_lines.Count == 0)
            throw new DomainException("An invoice needs at least one line before it is issued.");

        Status = InvoiceStatus.Issued;
    }

    public void Cancel()
    {
        if (Status is InvoiceStatus.Paid or InvoiceStatus.PartiallyPaid)
            throw new DomainException("An invoice with payments cannot be cancelled.");
        if (Status == InvoiceStatus.Cancelled)
            throw new DomainException("Invoice is already cancelled.");

        Status = InvoiceStatus.Cancelled;
    }

    private InvoiceLine AddLine(Guid? productId, string description, string hsnCode,
        decimal quantity, decimal unitPrice, decimal gstRate)
    {
        EnsureDraft();
        var line = new InvoiceLine(TenantId, Id, productId, description, hsnCode, quantity, unitPrice, gstRate);
        _lines.Add(line);
        return line;
    }

    private void EnsureDraft()
    {
        if (Status != InvoiceStatus.Draft)
            throw new DomainException("Only draft invoices can be changed.");
    }
}
