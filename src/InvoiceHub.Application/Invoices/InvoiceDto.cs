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
    decimal TaxableValue,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal IgstAmount);

public sealed record InvoiceDto(
    Guid Id,
    string InvoiceNumber,
    string Status,
    DateOnly IssueDate,
    DateOnly DueDate,
    Guid CustomerId,
    string CustomerName,
    string SellerStateCode,
    string PlaceOfSupplyStateCode,
    bool IsIntraState,
    string? Notes,
    IReadOnlyList<InvoiceLineDto> Lines,
    decimal TaxableTotal,
    decimal CgstTotal,
    decimal SgstTotal,
    decimal IgstTotal,
    decimal TaxTotal,
    decimal GrandTotal,
    decimal AmountPaid,
    decimal BalanceDue,
    bool IsOverdue);

internal static class InvoiceMapping
{
    public static InvoiceDto ToDto(this Invoice invoice, string customerName, DateOnly today) => new(
        invoice.Id,
        invoice.InvoiceNumber,
        invoice.Status.ToString(),
        invoice.IssueDate,
        invoice.DueDate,
        invoice.CustomerId,
        customerName,
        invoice.SellerStateCode,
        invoice.PlaceOfSupplyStateCode,
        invoice.IsIntraState,
        invoice.Notes,
        invoice.Lines
            .OrderBy(l => l.LineNumber)
            .Select(l => new InvoiceLineDto(
                l.Id, l.LineNumber, l.ProductId, l.Description, l.HsnCode,
                l.Quantity, l.UnitPrice, l.GstRate, l.TaxableValue,
                l.CgstAmount, l.SgstAmount, l.IgstAmount))
            .ToList(),
        invoice.TaxableTotal,
        invoice.CgstTotal,
        invoice.SgstTotal,
        invoice.IgstTotal,
        invoice.TaxTotal,
        invoice.GrandTotal,
        invoice.AmountPaid,
        invoice.BalanceDue,
        (invoice.Status == InvoiceStatus.Issued || invoice.Status == InvoiceStatus.PartiallyPaid) && invoice.DueDate < today);
}
