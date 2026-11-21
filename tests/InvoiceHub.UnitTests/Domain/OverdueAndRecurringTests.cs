using InvoiceHub.Domain.Common;
using InvoiceHub.Domain.Customers;
using InvoiceHub.Domain.Invoices;
using InvoiceHub.Domain.Products;

namespace InvoiceHub.UnitTests.Domain;

public sealed class InvoiceOverdueTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private static readonly DateOnly Due = new(2026, 11, 10);

    private Invoice Make(bool issue)
    {
        var customer = new Customer(_tenantId, "Buyer", "buyer@example.test", "27", new Address("1 Road", null, "Pune", "411001"));
        var invoice = new Invoice(_tenantId, "27", customer, "INV-1", new DateOnly(2026, 10, 25), Due);
        invoice.AddCustomLine("Consulting", "9983", 1, 1000m, 18m);
        if (issue)
            invoice.Issue();
        return invoice;
    }

    [Fact]
    public void An_unpaid_issued_invoice_past_its_due_date_is_marked_once()
    {
        var invoice = Make(issue: true);

        Assert.True(invoice.MarkOverdue(Due.AddDays(1)));
        Assert.Equal(Due.AddDays(1), invoice.OverdueMarkedOn);
        Assert.False(invoice.MarkOverdue(Due.AddDays(2)));
        Assert.Equal(Due.AddDays(1), invoice.OverdueMarkedOn);
    }

    [Fact]
    public void An_invoice_is_not_overdue_on_its_due_date()
    {
        var invoice = Make(issue: true);

        Assert.False(invoice.MarkOverdue(Due));
        Assert.Null(invoice.OverdueMarkedOn);
    }

    [Fact]
    public void A_draft_is_never_marked_overdue()
    {
        Assert.False(Make(issue: false).MarkOverdue(Due.AddDays(30)));
    }

    [Fact]
    public void A_part_paid_invoice_can_be_marked_but_a_paid_one_cannot()
    {
        var partly = Make(issue: true);
        partly.RecordPayment("e1", 100m, null, DateTime.UtcNow);
        Assert.True(partly.MarkOverdue(Due.AddDays(1)));

        var paid = Make(issue: true);
        paid.RecordPayment("e2", 1180m, null, DateTime.UtcNow);
        Assert.False(paid.MarkOverdue(Due.AddDays(1)));
    }

    [Fact]
    public void A_cancelled_invoice_is_not_marked_overdue()
    {
        var invoice = Make(issue: true);
        invoice.Cancel();

        Assert.False(invoice.MarkOverdue(Due.AddDays(1)));
    }
}

public sealed class RecurringInvoiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    private Customer Customer(Guid? tenant = null) =>
        new(tenant ?? _tenantId, "Buyer", "buyer@example.test", "27", new Address("1 Road", null, "Pune", "411001"));

    private Product Product() => new(_tenantId, "Retainer", "9983", 5000m, 18m);

    private RecurringInvoice Make(DateOnly first, int months = 1, DateOnly? end = null, int dueInDays = 15) =>
        new(_tenantId, Customer(), "Monthly retainer", months, first, dueInDays, end, [(Product(), 1m)]);

    [Fact]
    public void A_new_template_is_active_and_due_on_its_first_run_date()
    {
        var template = Make(new DateOnly(2026, 11, 5));

        Assert.True(template.IsActive);
        Assert.False(template.IsDue(new DateOnly(2026, 11, 4)));
        Assert.True(template.IsDue(new DateOnly(2026, 11, 5)));
        Assert.True(template.IsDue(new DateOnly(2027, 1, 1)));
    }

    [Fact]
    public void Advancing_keeps_the_day_of_the_month_even_after_a_short_month()
    {
        var template = Make(new DateOnly(2026, 1, 31));

        template.Advance();
        Assert.Equal(new DateOnly(2026, 2, 28), template.NextRunDate);
        template.Advance();
        Assert.Equal(new DateOnly(2026, 3, 31), template.NextRunDate);
    }

    [Fact]
    public void Advancing_by_a_quarter_moves_three_months()
    {
        var template = Make(new DateOnly(2026, 1, 15), months: 3);

        template.Advance();
        template.Advance();

        Assert.Equal(new DateOnly(2026, 7, 15), template.NextRunDate);
    }

    [Fact]
    public void The_template_stops_after_its_end_date()
    {
        var template = Make(new DateOnly(2026, 10, 1), end: new DateOnly(2026, 11, 15));

        template.Advance();   // 1 Nov, still inside
        Assert.True(template.IsActive);
        template.Advance();   // 1 Dec, past the end
        Assert.False(template.IsActive);
        Assert.False(template.IsDue(new DateOnly(2027, 1, 1)));
    }

    [Fact]
    public void A_paused_template_is_never_due()
    {
        var template = Make(new DateOnly(2026, 11, 5));
        template.Pause();

        Assert.False(template.IsDue(new DateOnly(2027, 1, 1)));
    }

    [Fact]
    public void The_invoice_number_is_deterministic_and_fits_the_column()
    {
        var template = Make(new DateOnly(2026, 11, 5));

        var number = template.NumberFor(new DateOnly(2026, 11, 5));

        Assert.Equal(number, template.NumberFor(new DateOnly(2026, 11, 5)));
        Assert.NotEqual(number, template.NumberFor(new DateOnly(2026, 12, 5)));
        Assert.True(number.Length <= Invoice.NumberMaxLength);
        Assert.EndsWith("-261105", number);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void The_interval_must_be_between_one_and_twelve_months(int months)
    {
        Assert.Throws<DomainException>(() => Make(new DateOnly(2026, 11, 5), months));
    }

    [Fact]
    public void An_end_date_before_the_first_run_is_rejected()
    {
        Assert.Throws<DomainException>(() => Make(new DateOnly(2026, 11, 5), end: new DateOnly(2026, 11, 1)));
    }

    [Fact]
    public void A_customer_from_another_tenant_is_rejected()
    {
        Assert.Throws<DomainException>(() => new RecurringInvoice(_tenantId, Customer(Guid.NewGuid()), "X", 1,
            new DateOnly(2026, 11, 5), 10, null, [(Product(), 1m)]));
    }

    [Fact]
    public void A_template_needs_at_least_one_line_and_a_positive_quantity()
    {
        Assert.Throws<DomainException>(() => new RecurringInvoice(_tenantId, Customer(), "X", 1,
            new DateOnly(2026, 11, 5), 10, null, []));
        Assert.Throws<DomainException>(() => new RecurringInvoice(_tenantId, Customer(), "X", 1,
            new DateOnly(2026, 11, 5), 10, null, [(Product(), 0m)]));
    }
}
