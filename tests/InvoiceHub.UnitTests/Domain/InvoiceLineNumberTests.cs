using InvoiceHub.Domain.Common;
using InvoiceHub.Domain.Customers;
using InvoiceHub.Domain.Invoices;
using InvoiceHub.Domain.Products;

namespace InvoiceHub.UnitTests.Domain;

public sealed class InvoiceLineNumberTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    private Invoice NewInvoice()
    {
        var customer = new Customer(_tenantId, "Buyer", "buyer@example.test", "27",
            new Address("1 Road", null, "Pune", "411001"));
        return new Invoice(_tenantId, customer, "INV-1", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));
    }

    private Product NewProduct(string name) => new(_tenantId, name, "9983", 100m, 18m);

    [Fact]
    public void Lines_are_numbered_in_the_order_they_are_added()
    {
        var invoice = NewInvoice();

        invoice.AddLine(NewProduct("A"), 1);
        invoice.AddCustomLine("B", "9983", 1, 50m, 18m);
        invoice.AddLine(NewProduct("C"), 1);

        Assert.Equal([1, 2, 3], invoice.Lines.Select(l => l.LineNumber));
        Assert.Equal(["A", "B", "C"], invoice.Lines.Select(l => l.Description));
    }

    [Fact]
    public void Removing_a_line_closes_the_gap_in_numbering()
    {
        var invoice = NewInvoice();
        invoice.AddLine(NewProduct("A"), 1);
        var middle = invoice.AddLine(NewProduct("B"), 1);
        invoice.AddLine(NewProduct("C"), 1);

        invoice.RemoveLine(middle.Id);

        Assert.Equal([1, 2], invoice.Lines.Select(l => l.LineNumber));
        Assert.Equal(["A", "C"], invoice.Lines.Select(l => l.Description));
    }

    [Fact]
    public void Lines_cannot_be_added_once_the_invoice_is_issued()
    {
        var invoice = NewInvoice();
        invoice.AddLine(NewProduct("A"), 1);
        invoice.Issue();

        Assert.Throws<DomainException>(() => invoice.AddLine(NewProduct("B"), 1));
    }

    [Fact]
    public void A_product_from_another_tenant_cannot_be_added()
    {
        var invoice = NewInvoice();
        var foreign = new Product(Guid.NewGuid(), "Foreign", "9983", 10m, 18m);

        Assert.Throws<DomainException>(() => invoice.AddLine(foreign, 1));
    }
}
