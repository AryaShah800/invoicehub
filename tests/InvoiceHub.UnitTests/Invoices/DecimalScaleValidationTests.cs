using FluentValidation.TestHelper;
using InvoiceHub.Application.Invoices.Commands.CreateInvoice;
using InvoiceHub.Application.Products.Commands;

namespace InvoiceHub.UnitTests.Invoices;

/// <summary>
/// Values must fit the column scale (quantity decimal(18,3), price decimal(18,2)); otherwise the
/// database rounds them on save and Quantity x UnitPrice no longer matches the stored taxable value.
/// </summary>
public sealed class DecimalScaleValidationTests
{
    private static CreateInvoiceCommand WithLine(CreateInvoiceLine line) => new(
        Guid.NewGuid(), "INV-001", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), null, [line]);

    [Fact]
    public void A_quantity_with_more_than_three_decimals_is_rejected()
    {
        var result = new CreateInvoiceCommandValidator().TestValidate(
            WithLine(new CreateInvoiceLine(Guid.NewGuid(), null, null, 1.1234m, null, null)));

        result.ShouldHaveValidationErrorFor("Lines[0].Quantity");
    }

    [Fact]
    public void A_custom_price_with_more_than_two_decimals_is_rejected()
    {
        var result = new CreateInvoiceCommandValidator().TestValidate(
            WithLine(new CreateInvoiceLine(null, "Consulting", "9983", 1m, 10.999m, 18m)));

        result.ShouldHaveValidationErrorFor("Lines[0].UnitPrice");
    }

    [Fact]
    public void Trailing_zeros_do_not_count_as_extra_decimals()
    {
        var result = new CreateInvoiceCommandValidator().TestValidate(
            WithLine(new CreateInvoiceLine(null, "Consulting", "9983", 1.5000m, 10.500m, 18m)));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void A_product_price_with_more_than_two_decimals_is_rejected()
    {
        var result = new CreateProductCommandValidator().TestValidate(
            new CreateProductCommand("Widget", "8471", 10.999m, 18m));

        result.ShouldHaveValidationErrorFor(c => c.UnitPrice);
    }
}
