using FluentValidation;
using InvoiceHub.Domain.Common;
using InvoiceHub.Domain.Invoices;

namespace InvoiceHub.Application.Invoices.Commands.CreateInvoice;

public sealed class CreateInvoiceCommandValidator : AbstractValidator<CreateInvoiceCommand>
{
    public const int MaxLines = 200;
    public const decimal MaxQuantity = 1_000_000m;
    public const decimal MaxUnitPrice = 1_000_000_000m;

    public CreateInvoiceCommandValidator()
    {
        RuleFor(c => c.CustomerId).NotEmpty();

        RuleFor(c => c.InvoiceNumber)
            .NotEmpty()
            .MaximumLength(Invoice.NumberMaxLength);

        RuleFor(c => c.DueDate)
            .GreaterThanOrEqualTo(c => c.IssueDate)
            .WithMessage("Due date cannot be before the issue date.");

        RuleFor(c => c.Notes).MaximumLength(Invoice.NotesMaxLength);

        RuleFor(c => c.Lines)
            .NotNull()
            .NotEmpty().WithMessage("An invoice needs at least one line.")
            .Must(lines => lines is null || lines.Count <= MaxLines)
            .WithMessage($"An invoice can have at most {MaxLines} lines.");

        RuleForEach(c => c.Lines).ChildRules(line =>
        {
            // Must fit decimal(18,3) / decimal(18,2), or the database rounds the stored value
            // and Quantity x UnitPrice stops matching the taxable value computed here.
            line.RuleFor(l => l.Quantity)
                .GreaterThan(0)
                .LessThanOrEqualTo(MaxQuantity)
                .PrecisionScale(18, 3, ignoreTrailingZeros: true);

            // Catalogue line: everything but the quantity comes from the product, so nothing else may be sent.
            line.When(l => l.ProductId.HasValue, () =>
            {
                line.RuleFor(l => l.ProductId).NotEqual(Guid.Empty);
                line.RuleFor(l => l.Description).Null().WithMessage("A catalogue line cannot override the description.");
                line.RuleFor(l => l.HsnCode).Null().WithMessage("A catalogue line cannot override the HSN code.");
                line.RuleFor(l => l.UnitPrice).Null().WithMessage("A catalogue line cannot override the price.");
                line.RuleFor(l => l.GstRate).Null().WithMessage("A catalogue line cannot override the GST rate.");
            });

            // Custom line: it must describe itself completely.
            line.When(l => !l.ProductId.HasValue, () =>
            {
                line.RuleFor(l => l.Description)
                    .NotEmpty()
                    .MaximumLength(InvoiceLine.DescriptionMaxLength);
                line.RuleFor(l => l.HsnCode)
                    .NotEmpty()
                    .Matches(@"^(\d{4}|\d{6}|\d{8})$").WithMessage("HSN code must be 4, 6 or 8 digits.");
                line.RuleFor(l => l.UnitPrice)
                    .NotNull().WithMessage("A custom line needs a unit price.")
                    .GreaterThanOrEqualTo(0).LessThanOrEqualTo(MaxUnitPrice)
                    .PrecisionScale(18, 2, ignoreTrailingZeros: true);
                line.RuleFor(l => l.GstRate)
                    .NotNull().WithMessage("A custom line needs a GST rate.")
                    .Must(rate => rate is null || GstRules.ValidRates.Contains(rate.Value))
                    .WithMessage("GST rate must be one of 0, 0.25, 3, 5, 12, 18 or 28.");
            });
        });
    }
}
