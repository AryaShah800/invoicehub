namespace InvoiceHub.Application.Invoices.Pdf;

/// <summary>Everything the PDF needs, gathered by the query so the renderer has no database access.</summary>
public sealed record InvoicePdfModel(
    InvoiceDto Invoice,
    string SellerName,
    string SellerGstin,
    string CustomerGstin,
    string CustomerAddress,
    string CustomerStateCode);

public interface IInvoicePdfRenderer
{
    byte[] Render(InvoicePdfModel model);
}
