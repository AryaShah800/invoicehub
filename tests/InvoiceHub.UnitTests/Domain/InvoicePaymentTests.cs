using InvoiceHub.Domain.Common;
using InvoiceHub.Domain.Customers;
using InvoiceHub.Domain.Invoices;

namespace InvoiceHub.UnitTests.Domain;

public sealed class InvoicePaymentTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);
    private readonly Guid _tenantId = Guid.NewGuid();

    private Invoice NewInvoice(string number)
    {
        var customer = new Customer(_tenantId, "Buyer", "buyer@example.test", "27", new Address("1 Road", null, "Pune", "411001"));
        var invoice = new Invoice(_tenantId, "27", customer, number, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));
        invoice.AddCustomLine("Consulting", "9983", 1, 1000m, 18m);
        return invoice;
    }

    /// <summary>An issued invoice of 1000.00 plus 18% GST: grand total 1180.00.</summary>
    private Invoice IssuedInvoice()
    {
        var invoice = NewInvoice("INV-1");
        invoice.Issue();
        return invoice;
    }

    [Fact]
    public void A_new_invoice_has_nothing_paid_and_the_whole_total_due()
    {
        var invoice = IssuedInvoice();

        Assert.Equal(0m, invoice.AmountPaid);
        Assert.Equal(1180m, invoice.BalanceDue);
    }

    [Fact]
    public void A_part_payment_makes_the_invoice_partially_paid()
    {
        var invoice = IssuedInvoice();

        var payment = invoice.RecordPayment("evt_1", 500m, "UPI-123", Now);

        Assert.Equal(InvoiceStatus.PartiallyPaid, invoice.Status);
        Assert.Equal(500m, invoice.AmountPaid);
        Assert.Equal(680m, invoice.BalanceDue);
        Assert.Equal((invoice.Id, _tenantId, "evt_1", 500m, "UPI-123"),
            (payment.InvoiceId, payment.TenantId, payment.EventId, payment.Amount, payment.Reference));
    }

    [Fact]
    public void Paying_the_balance_in_two_steps_makes_the_invoice_paid()
    {
        var invoice = IssuedInvoice();

        invoice.RecordPayment("evt_1", 500m, null, Now);
        invoice.RecordPayment("evt_2", 680m, null, Now);

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(0m, invoice.BalanceDue);
    }

    [Fact]
    public void Paying_the_full_amount_at_once_makes_the_invoice_paid()
    {
        var invoice = IssuedInvoice();

        invoice.RecordPayment("evt_1", 1180m, null, Now);

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
    }

    [Fact]
    public void A_payment_larger_than_the_balance_is_rejected_and_changes_nothing()
    {
        var invoice = IssuedInvoice();
        invoice.RecordPayment("evt_1", 1000m, null, Now);

        var ex = Assert.Throws<DomainException>(() => invoice.RecordPayment("evt_2", 180.01m, null, Now));

        Assert.Contains("balance due", ex.Message);
        Assert.Equal(1000m, invoice.AmountPaid);
        Assert.Equal(InvoiceStatus.PartiallyPaid, invoice.Status);
    }

    [Fact]
    public void A_paid_invoice_cannot_be_paid_again()
    {
        var invoice = IssuedInvoice();
        invoice.RecordPayment("evt_1", 1180m, null, Now);

        var ex = Assert.Throws<DomainException>(() => invoice.RecordPayment("evt_2", 1m, null, Now));

        Assert.Contains("already paid", ex.Message);
    }

    [Fact]
    public void A_draft_invoice_cannot_receive_payments()
    {
        var draft = NewInvoice("INV-2");

        Assert.Throws<DomainException>(() => draft.RecordPayment("evt_1", 100m, null, Now));
    }

    [Fact]
    public void A_cancelled_invoice_cannot_receive_payments()
    {
        var invoice = IssuedInvoice();
        invoice.Cancel();

        Assert.Throws<DomainException>(() => invoice.RecordPayment("evt_1", 100m, null, Now));
    }

    [Fact]
    public void A_part_paid_invoice_cannot_be_cancelled()
    {
        var invoice = IssuedInvoice();
        invoice.RecordPayment("evt_1", 1m, null, Now);

        Assert.Throws<DomainException>(invoice.Cancel);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10.005)]
    public void Amounts_must_be_positive_with_at_most_two_decimals(double amount)
    {
        var invoice = IssuedInvoice();

        Assert.Throws<DomainException>(() => invoice.RecordPayment("evt_1", (decimal)amount, null, Now));
        Assert.Equal(0m, invoice.AmountPaid);
    }

    [Fact]
    public void An_event_id_is_required()
    {
        var invoice = IssuedInvoice();

        Assert.Throws<DomainException>(() => invoice.RecordPayment(" ", 10m, null, Now));
    }
}
