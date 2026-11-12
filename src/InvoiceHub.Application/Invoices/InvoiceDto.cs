using InvoiceHub.Domain.Invoices;

namespace InvoiceHub.Application.Invoices;

public sealed record InvoiceLineDto(
    Guid Id,
    int LineNumber,
    Guid? ProductId,
    string Description,
    string HsnCode,
    decimal Quantity,
    decimal UnitPrice,
    decimal GstRate,
    decimal TaxableValue);

public sealed record InvoiceDto(
    Guid Id,
    string InvoiceNumber,
    string Status,
    DateOnly IssueDate,
    DateOnly DueDate,
    Guid CustomerId,
    string CustomerName,
    string PlaceOfSupplyStateCode,
    string? Notes,
    IReadOnlyList<InvoiceLineDto> Lines,
    decimal TaxableTotal);

internal static class InvoiceMapping
{
    public static InvoiceDto ToDto(this Invoice invoice, string customerName) => new(
        invoice.Id,
        invoice.InvoiceNumber,
        invoice.Status.ToString(),
        invoice.IssueDate,
        invoice.DueDate,
        invoice.CustomerId,
        customerName,
        invoice.PlaceOfSupplyStateCode,
        invoice.Notes,
        invoice.Lines
            .OrderBy(l => l.LineNumber)
            .Select(l => new InvoiceLineDto(
                l.Id, l.LineNumber, l.ProductId, l.Description, l.HsnCode,
                l.Quantity, l.UnitPrice, l.GstRate, l.TaxableValue))
            .ToList(),
        invoice.TaxableTotal);
}
