using System.ComponentModel.DataAnnotations;
using InvoiceHub.Application.Invoices.Commands.CreateInvoice;

namespace InvoiceHub.Web.Services;

public sealed class LineForm
{
    public Guid? ProductId { get; set; }

    public decimal Quantity { get; set; } = 1;
}

/// <summary>
/// What the create-invoice screen edits. Every rule is in Validate (not attributes) because DataAnnotations skips
/// IValidatableObject when a property already failed, and the user should see all problems at once. The API checks everything again.
/// </summary>
public sealed class InvoiceForm : IValidatableObject
{
    public Guid? CustomerId { get; set; }

    public string InvoiceNumber { get; set; } = "";

    public DateOnly IssueDate { get; set; }

    public DateOnly DueDate { get; set; }

    public string? Notes { get; set; }

    public List<LineForm> Lines { get; set; } = [new()];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CustomerId is null)
            yield return new ValidationResult("Choose a customer.", [nameof(CustomerId)]);

        if (string.IsNullOrWhiteSpace(InvoiceNumber))
            yield return new ValidationResult("Enter an invoice number.", [nameof(InvoiceNumber)]);
        else if (InvoiceNumber.Trim().Length > 16)
            yield return new ValidationResult("The invoice number can be at most 16 characters.", [nameof(InvoiceNumber)]);

        if (Notes is { Length: > 1000 })
            yield return new ValidationResult("Notes can be at most 1000 characters.", [nameof(Notes)]);

        if (DueDate < IssueDate)
            yield return new ValidationResult("The due date cannot be before the issue date.", [nameof(DueDate)]);

        if (Lines.Count == 0)
            yield return new ValidationResult("Add at least one line.", [nameof(Lines)]);

        for (var i = 0; i < Lines.Count; i++)
        {
            if (Lines[i].ProductId is null)
                yield return new ValidationResult($"Line {i + 1}: choose a product.", [nameof(Lines)]);
            if (Lines[i].Quantity <= 0)
                yield return new ValidationResult($"Line {i + 1}: quantity must be more than zero.", [nameof(Lines)]);
        }
    }

    public CreateInvoiceCommand ToCommand() => new(
        CustomerId!.Value,
        InvoiceNumber.Trim(),
        IssueDate,
        DueDate,
        string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
        Lines.Select(l => new CreateInvoiceLine(l.ProductId, null, null, l.Quantity, null, null)).ToList());
}
