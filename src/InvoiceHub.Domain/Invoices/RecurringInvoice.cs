using InvoiceHub.Domain.Common;
using InvoiceHub.Domain.Customers;
using InvoiceHub.Domain.Products;

namespace InvoiceHub.Domain.Invoices;

/// <summary>One product and quantity that is repeated on every invoice generated from a template.</summary>
public sealed class RecurringInvoiceLine : Entity
{
    private RecurringInvoiceLine()
    {
    }

    internal RecurringInvoiceLine(Guid productId, decimal quantity)
    {
        ProductId = Guard.NotEmpty(productId, "Product");
        Quantity = Guard.Positive(quantity, "Quantity");
    }

    public Guid RecurringInvoiceId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }
}

/// <summary>
/// A template that raises the same invoice every N months, for example a monthly retainer. The background job
/// generates one invoice per due occurrence and moves <see cref="NextRunDate"/> forward, so running the job
/// twice for the same day does nothing the second time.
/// </summary>
public sealed class RecurringInvoice : AuditableEntity, ITenantOwned
{
    public const int NameMaxLength = 100;
    public const int MaxIntervalMonths = 12;
    public const int MaxLines = 50;

    private readonly List<RecurringInvoiceLine> _lines = [];

    private RecurringInvoice()
    {
        Name = null!;
    }

    public RecurringInvoice(Guid tenantId, Customer customer, string name, int intervalMonths, DateOnly firstRunDate,
        int dueInDays, DateOnly? endDate, IEnumerable<(Product Product, decimal Quantity)> lines)
    {
        TenantId = Guard.NotEmpty(tenantId, "Tenant");
        if (customer is null || customer.TenantId != tenantId)
            throw new DomainException("Customer belongs to a different tenant.");
        if (intervalMonths is < 1 or > MaxIntervalMonths)
            throw new DomainException($"Interval must be between 1 and {MaxIntervalMonths} months.");
        if (dueInDays is < 0 or > 365)
            throw new DomainException("Days until due must be between 0 and 365.");
        if (endDate is { } end && end < firstRunDate)
            throw new DomainException("End date cannot be before the first run date.");

        CustomerId = customer.Id;
        Name = Guard.NotBlank(name, "Name", NameMaxLength);
        IntervalMonths = intervalMonths;
        FirstRunDate = firstRunDate;
        NextRunDate = firstRunDate;
        DueInDays = dueInDays;
        EndDate = endDate;
        IsActive = true;

        foreach (var (product, quantity) in lines)
        {
            if (product.TenantId != tenantId)
                throw new DomainException("Product belongs to a different tenant.");
            if (!product.IsActive)
                throw new DomainException("Product is inactive.");
            _lines.Add(new RecurringInvoiceLine(product.Id, quantity));
        }
        if (_lines.Count is 0 or > MaxLines)
            throw new DomainException($"A recurring invoice needs between 1 and {MaxLines} lines.");
    }

    public Guid TenantId { get; private set; }
    public Guid CustomerId { get; private set; }
    public string Name { get; private set; }
    public int IntervalMonths { get; private set; }
    public DateOnly FirstRunDate { get; private set; }
    public DateOnly NextRunDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public int DueInDays { get; private set; }
    public bool IsActive { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<RecurringInvoiceLine> Lines => _lines.AsReadOnly();

    /// <summary>True when an occurrence is due on or before <paramref name="today"/>.</summary>
    public bool IsDue(DateOnly today) => IsActive && NextRunDate <= today && (EndDate is null || NextRunDate <= EndDate);

    /// <summary>
    /// Deterministic invoice number for one occurrence. Because the number is unique per tenant, even two job
    /// instances racing on the same template cannot create the same occurrence twice.
    /// </summary>
    public string NumberFor(DateOnly occurrence) => $"R{Id.ToString("N")[..6]}-{occurrence:yyMMdd}";

    /// <summary>Moves to the next occurrence, and deactivates the template once it passes its end date.</summary>
    public void Advance()
    {
        NextRunDate = FirstRunDate.AddMonths(IntervalMonths * MonthsSinceFirst(NextRunDate) + IntervalMonths);
        if (EndDate is { } end && NextRunDate > end)
            IsActive = false;
    }

    public void Pause() => IsActive = false;

    private int MonthsSinceFirst(DateOnly date) =>
        ((date.Year - FirstRunDate.Year) * 12 + date.Month - FirstRunDate.Month) / IntervalMonths;
}
