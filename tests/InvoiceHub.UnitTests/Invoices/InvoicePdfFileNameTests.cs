using InvoiceHub.Application.Invoices.Pdf;

namespace InvoiceHub.UnitTests.Invoices;

public sealed class InvoicePdfFileNameTests
{
    private static readonly Guid Id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

    [Theory]
    [InlineData("INV-2026/001", "invoice-INV-2026001.pdf")]
    [InlineData("A_1", "invoice-A_1.pdf")]
    [InlineData("///", "invoice-0f8fad5b.pdf")]
    [InlineData("१२३", "invoice-0f8fad5b.pdf")]
    [InlineData("INV \"x\"", "invoice-INVx.pdf")]
    public void File_name_is_ascii_and_never_empty(string number, string expected) =>
        Assert.Equal(expected, GetInvoicePdfQueryHandler.FileNameFor(number, Id));
}
