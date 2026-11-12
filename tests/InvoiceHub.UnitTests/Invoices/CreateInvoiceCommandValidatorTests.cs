using FluentValidation.TestHelper;
using InvoiceHub.Application.Invoices.Commands.CreateInvoice;

namespace InvoiceHub.UnitTests.Invoices;

public sealed class CreateInvoiceCommandValidatorTests
{
    private readonly CreateInvoiceCommandValidator _validator = new();

    private static CreateInvoiceLine CatalogueLine(decimal quantity = 2) =>
        new(Guid.NewGuid(), null, null, quantity, null, null);

    private static CreateInvoiceLine CustomLine(
        string? description = "Consulting", string? hsn = "9983", decimal? price = 500m, decimal? rate = 18m, decimal quantity = 1) =>
        new(null, description, hsn, quantity, price, rate);

    private static CreateInvoiceCommand Valid(params CreateInvoiceLine[] lines) => new(
        Guid.NewGuid(), "INV-001", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), null,
        lines.Length == 0 ? [CatalogueLine()] : lines);

    [Fact]
    public void A_catalogue_line_and_a_custom_line_are_valid()
    {
        var result = _validator.TestValidate(Valid(CatalogueLine(), CustomLine()));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void An_invoice_needs_at_least_one_line()
    {
        var command = Valid() with { Lines = [] };

        _validator.TestValidate(command).ShouldHaveValidationErrorFor(c => c.Lines);
    }

    [Fact]
    public void Too_many_lines_are_rejected()
    {
        var command = Valid() with
        {
            Lines = Enumerable.Range(0, CreateInvoiceCommandValidator.MaxLines + 1).Select(_ => CatalogueLine()).ToList(),
        };

        _validator.TestValidate(command).ShouldHaveValidationErrorFor(c => c.Lines);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("THIS-NUMBER-IS-FAR-TOO-LONG")]
    public void A_missing_or_overlong_invoice_number_is_rejected(string number)
    {
        var command = Valid() with { InvoiceNumber = number };

        _validator.TestValidate(command).ShouldHaveValidationErrorFor(c => c.InvoiceNumber);
    }

    [Fact]
    public void The_due_date_cannot_be_before_the_issue_date()
    {
        var command = Valid() with { IssueDate = new DateOnly(2026, 11, 10), DueDate = new DateOnly(2026, 11, 9) };

        _validator.TestValidate(command).ShouldHaveValidationErrorFor(c => c.DueDate);
    }

    [Fact]
    public void An_empty_customer_id_is_rejected()
    {
        var command = Valid() with { CustomerId = Guid.Empty };

        _validator.TestValidate(command).ShouldHaveValidationErrorFor(c => c.CustomerId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_000_001)]
    public void Quantity_must_be_positive_and_bounded(double quantity)
    {
        var result = _validator.TestValidate(Valid(CatalogueLine((decimal)quantity)));

        result.ShouldHaveValidationErrorFor("Lines[0].Quantity");
    }

    [Fact]
    public void A_catalogue_line_cannot_override_the_price_rate_description_or_hsn()
    {
        var line = new CreateInvoiceLine(Guid.NewGuid(), "Sneaky", "9983", 1, 1m, 0m);

        var result = _validator.TestValidate(Valid(line));

        result.ShouldHaveValidationErrorFor("Lines[0].Description");
        result.ShouldHaveValidationErrorFor("Lines[0].HsnCode");
        result.ShouldHaveValidationErrorFor("Lines[0].UnitPrice");
        result.ShouldHaveValidationErrorFor("Lines[0].GstRate");
    }

    [Fact]
    public void A_catalogue_line_with_an_empty_product_id_is_rejected()
    {
        var line = new CreateInvoiceLine(Guid.Empty, null, null, 1, null, null);

        _validator.TestValidate(Valid(line)).ShouldHaveValidationErrorFor("Lines[0].ProductId");
    }

    [Fact]
    public void A_custom_line_must_describe_itself_completely()
    {
        var result = _validator.TestValidate(Valid(CustomLine(description: "", hsn: null, price: null, rate: null)));

        result.ShouldHaveValidationErrorFor("Lines[0].Description");
        result.ShouldHaveValidationErrorFor("Lines[0].HsnCode");
        result.ShouldHaveValidationErrorFor("Lines[0].UnitPrice");
        result.ShouldHaveValidationErrorFor("Lines[0].GstRate");
    }

    [Theory]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("ABCD")]
    public void A_custom_line_needs_a_4_6_or_8_digit_hsn_code(string hsn)
    {
        _validator.TestValidate(Valid(CustomLine(hsn: hsn))).ShouldHaveValidationErrorFor("Lines[0].HsnCode");
    }

    [Theory]
    [InlineData(7)]
    [InlineData(-5)]
    [InlineData(18.5)]
    public void A_custom_line_needs_a_real_gst_slab_rate(double rate)
    {
        _validator.TestValidate(Valid(CustomLine(rate: (decimal)rate))).ShouldHaveValidationErrorFor("Lines[0].GstRate");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.25)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(12)]
    [InlineData(18)]
    [InlineData(28)]
    public void Every_real_gst_slab_rate_is_accepted(double rate)
    {
        _validator.TestValidate(Valid(CustomLine(rate: (decimal)rate))).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void A_negative_custom_price_is_rejected()
    {
        _validator.TestValidate(Valid(CustomLine(price: -1m))).ShouldHaveValidationErrorFor("Lines[0].UnitPrice");
    }
}
