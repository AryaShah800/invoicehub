using InvoiceHub.Domain.Common;
using InvoiceHub.Domain.Customers;
using InvoiceHub.Domain.Invoices;

namespace InvoiceHub.UnitTests.Domain;

public sealed class InvoiceGstTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    private Invoice NewInvoice(string sellerState, string customerState)
    {
        var customer = new Customer(_tenantId, "Buyer", "buyer@example.test", customerState,
            new Address("1 Road", null, "Town", "411001"));
        return new Invoice(_tenantId, sellerState, customer, "INV-1", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));
    }

    [Fact]
    public void Same_state_invoice_is_intra_state_and_lines_carry_CGST_and_SGST()
    {
        var invoice = NewInvoice("27", "27");

        var line = invoice.AddCustomLine("Consulting", "9983", 1, 1000m, 18m);

        Assert.True(invoice.IsIntraState);
        Assert.Equal((90m, 90m, 0m), (line.CgstAmount, line.SgstAmount, line.IgstAmount));
    }

    [Fact]
    public void Different_state_invoice_lines_carry_IGST()
    {
        var invoice = NewInvoice("27", "24");

        var line = invoice.AddCustomLine("Consulting", "9983", 1, 1000m, 18m);

        Assert.False(invoice.IsIntraState);
        Assert.Equal((0m, 0m, 180m), (line.CgstAmount, line.SgstAmount, line.IgstAmount));
    }

    [Fact]
    public void Totals_add_up_the_lines_that_were_each_rounded_on_their_own()
    {
        var invoice = NewInvoice("27", "24");
        // 0.33 x 5% = 0.0165 -> 0.02 per line. Three lines: 0.06, not round(0.0495) = 0.05.
        for (var i = 0; i < 3; i++)
            invoice.AddCustomLine($"Item {i}", "9983", 1, 0.33m, 5m);

        Assert.Equal(0.06m, invoice.IgstTotal);
        Assert.Equal(0.99m, invoice.TaxableTotal);
        Assert.Equal(1.05m, invoice.GrandTotal);
    }

    [Fact]
    public void Seller_state_must_be_a_valid_state_code()
    {
        Assert.Throws<DomainException>(() => NewInvoice("", "27"));
        Assert.Throws<DomainException>(() => NewInvoice("MH", "27"));
    }
}
