using System.ComponentModel.DataAnnotations;
using InvoiceHub.Web.Services;

namespace InvoiceHub.Web.Tests;

public sealed class InvoiceFormTests
{
    private static InvoiceForm Valid() => new()
    {
        CustomerId = Guid.NewGuid(),
        InvoiceNumber = "INV-1",
        IssueDate = new DateOnly(2026, 11, 1),
        DueDate = new DateOnly(2026, 11, 16),
        Lines = [new LineForm { ProductId = Guid.NewGuid(), Quantity = 2 }],
    };

    private static List<string> Errors(InvoiceForm form)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(form, new ValidationContext(form), results, validateAllProperties: true);
        return results.Select(r => r.ErrorMessage!).ToList();
    }

    [Fact]
    public void A_complete_form_is_valid() => Assert.Empty(Errors(Valid()));

    [Fact]
    public void Customer_and_number_are_required()
    {
        var form = Valid();
        form.CustomerId = null;
        form.InvoiceNumber = "";

        var errors = Errors(form);

        Assert.Contains("Choose a customer.", errors);
        Assert.Contains("Enter an invoice number.", errors);
    }

    [Fact]
    public void The_due_date_cannot_be_before_the_issue_date()
    {
        var form = Valid();
        form.DueDate = form.IssueDate.AddDays(-1);

        Assert.Contains("The due date cannot be before the issue date.", Errors(form));
    }

    [Fact]
    public void Each_line_needs_a_product_and_a_positive_quantity()
    {
        var form = Valid();
        form.Lines = [new LineForm { ProductId = null, Quantity = 1 }, new LineForm { ProductId = Guid.NewGuid(), Quantity = 0 }];

        var errors = Errors(form);

        Assert.Contains("Line 1: choose a product.", errors);
        Assert.Contains("Line 2: quantity must be more than zero.", errors);
    }

    [Fact]
    public void An_invoice_without_lines_is_invalid()
    {
        var form = Valid();
        form.Lines = [];

        Assert.Contains("Add at least one line.", Errors(form));
    }

    [Fact]
    public void The_command_carries_catalogue_lines_and_trims_text()
    {
        var form = Valid();
        form.InvoiceNumber = "  INV-9 ";
        form.Notes = "   ";

        var command = form.ToCommand();

        Assert.Equal("INV-9", command.InvoiceNumber);
        Assert.Null(command.Notes);
        var line = Assert.Single(command.Lines);
        Assert.Equal(form.Lines[0].ProductId, line.ProductId);
        Assert.Equal(2m, line.Quantity);
        Assert.Null(line.UnitPrice);
    }
}
