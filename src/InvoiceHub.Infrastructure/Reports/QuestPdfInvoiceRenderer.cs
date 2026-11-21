using System.Globalization;
using InvoiceHub.Application.Invoices.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace InvoiceHub.Infrastructure.Reports;

/// <summary>
/// Draws a GST tax invoice with QuestPDF. QuestPDF is used under its Community licence (free for individuals and
/// small organisations); see https://www.questpdf.com/license/.
/// </summary>
public sealed class QuestPdfInvoiceRenderer : IInvoicePdfRenderer
{
    private static readonly CultureInfo India = new("en-IN");
    private const string Ink = "#1c2330";
    private const string Muted = "#6b7585";
    private const string Accent = "#1f4f8f";

    static QuestPdfInvoiceRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] Render(InvoicePdfModel model)
    {
        var inv = model.Invoice;
        return Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(t => t.FontSize(9.5f).FontColor(Ink).FontFamily("Arial"));

            page.Header().Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text(model.SellerName).FontSize(16).Bold().FontColor(Accent);
                        c.Item().Text($"GSTIN {model.SellerGstin}  |  State code {inv.SellerStateCode}").FontColor(Muted);
                    });
                    row.ConstantItem(190).AlignRight().Column(c =>
                    {
                        c.Item().Text("TAX INVOICE").FontSize(15).Bold();
                        c.Item().Text($"No. {inv.InvoiceNumber}");
                        c.Item().Text($"Date {inv.IssueDate:dd MMM yyyy}");
                        c.Item().Text($"Due {inv.DueDate:dd MMM yyyy}");
                        c.Item().Text($"Status {inv.Status}").FontColor(Muted);
                    });
                });
                col.Item().PaddingVertical(8).LineHorizontal(1).LineColor(Accent);
            });

            page.Content().Column(col =>
            {
                col.Spacing(10);
                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("Bill to").Bold().FontColor(Muted);
                        c.Item().Text(inv.CustomerName).Bold();
                        c.Item().Text(model.CustomerAddress);
                        c.Item().Text($"GSTIN {model.CustomerGstin}");
                    });
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("Place of supply").Bold().FontColor(Muted);
                        c.Item().Text($"State code {inv.PlaceOfSupplyStateCode}");
                        c.Item().Text(inv.IsIntraState ? "Within state: CGST + SGST" : "Inter-state: IGST").FontColor(Muted);
                    });
                });

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(22); c.RelativeColumn(4); c.ConstantColumn(44); c.ConstantColumn(34);
                        c.ConstantColumn(58); c.ConstantColumn(62); c.ConstantColumn(34); c.ConstantColumn(62);
                    });
                    static IContainer Head(IContainer c) => c.Background("#eef2f8").PaddingVertical(4).PaddingHorizontal(3).BorderBottom(1).BorderColor("#c9d3e3");
                    static IContainer Cell(IContainer c) => c.PaddingVertical(4).PaddingHorizontal(3).BorderBottom(0.5f).BorderColor("#e1e6ee");
                    table.Header(h =>
                    {
                        h.Cell().Element(Head).Text("#").Bold();
                        h.Cell().Element(Head).Text("Description").Bold();
                        h.Cell().Element(Head).Text("HSN").Bold();
                        h.Cell().Element(Head).AlignRight().Text("Qty").Bold();
                        h.Cell().Element(Head).AlignRight().Text("Rate").Bold();
                        h.Cell().Element(Head).AlignRight().Text("Taxable").Bold();
                        h.Cell().Element(Head).AlignRight().Text("GST%").Bold();
                        h.Cell().Element(Head).AlignRight().Text("Tax").Bold();
                    });
                    foreach (var l in inv.Lines)
                    {
                        table.Cell().Element(Cell).Text(l.LineNumber.ToString(India));
                        table.Cell().Element(Cell).Text(l.Description);
                        table.Cell().Element(Cell).Text(l.HsnCode);
                        table.Cell().Element(Cell).AlignRight().Text(Number(l.Quantity));
                        table.Cell().Element(Cell).AlignRight().Text(Money(l.UnitPrice));
                        table.Cell().Element(Cell).AlignRight().Text(Money(l.TaxableValue));
                        table.Cell().Element(Cell).AlignRight().Text(Number(l.GstRate));
                        table.Cell().Element(Cell).AlignRight().Text(Money(l.CgstAmount + l.SgstAmount + l.IgstAmount));
                    }
                });

                col.Item().AlignRight().Width(230).Column(c =>
                {
                    Total(c, "Taxable value", inv.TaxableTotal);
                    if (inv.IsIntraState)
                    {
                        Total(c, "CGST", inv.CgstTotal);
                        Total(c, "SGST", inv.SgstTotal);
                    }
                    else
                    {
                        Total(c, "IGST", inv.IgstTotal);
                    }
                    c.Item().PaddingTop(4).BorderTop(1).BorderColor(Ink).Row(r =>
                    {
                        r.RelativeItem().Text("Grand total").Bold().FontSize(11);
                        r.AutoItem().Text(Money(inv.GrandTotal)).Bold().FontSize(11);
                    });
                    Total(c, "Paid", inv.AmountPaid);
                    Total(c, "Balance due", inv.BalanceDue, bold: true);
                });

                col.Item().Text(t =>
                {
                    t.Span("Amount in words: ").FontColor(Muted);
                    t.Span(AmountInWords.ToIndianRupees(inv.GrandTotal)).Bold();
                });
                if (!string.IsNullOrWhiteSpace(inv.Notes))
                    col.Item().Text(t => { t.Span("Notes: ").FontColor(Muted); t.Span(inv.Notes); });
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(8).FontColor(Muted));
                t.Span("Computer generated invoice. Page ");
                t.CurrentPageNumber();
                t.Span(" of ");
                t.TotalPages();
            });
        })).GeneratePdf();
    }

    private static void Total(ColumnDescriptor c, string label, decimal value, bool bold = false) =>
        c.Item().Row(r =>
        {
            var l = r.RelativeItem().Text(label);
            var v = r.AutoItem().Text(Money(value));
            if (bold) { l.Bold(); v.Bold(); }
        });

    private static string Money(decimal value) => "Rs " + value.ToString("N2", India);
    private static string Number(decimal value) => value.ToString("0.##", India);
}
