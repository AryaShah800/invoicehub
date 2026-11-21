using System.Net;
using System.Net.Http.Json;
using InvoiceHub.Application.Auth;
using InvoiceHub.Application.Invoices;
using InvoiceHub.Application.Invoices.Commands.CreateInvoice;
using InvoiceHub.Domain.Customers;
using InvoiceHub.Domain.Products;

namespace InvoiceHub.IntegrationTests.Api;

[Collection(ApiCollection.Name)]
public sealed class InvoicePdfTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<(AuthResponse Auth, InvoiceDto Invoice)> NewInvoiceAsync()
    {
        var auth = await _client.RegisterAsync();
        await using var db = factory.CreateDbContext(auth.TenantId);
        var customer = new Customer(auth.TenantId, "Buyer Pvt Ltd", TestData.NewEmail(), "27", new Address("1 Main Road", null, "Pune", "411001"));
        var product = new Product(auth.TenantId, "Widget", "8471", 1000m, 18m);
        db.AddRange(customer, product);
        await db.SaveChangesAsync();

        var command = new CreateInvoiceCommand(customer.Id, $"INV-{Guid.NewGuid():N}"[..14], new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30),
            "Thanks", [new CreateInvoiceLine(product.Id, null, null, 2, null, null)]);
        var response = await _client.PostAsync("/api/invoices", command, auth.AccessToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (auth, (await response.Content.ReadFromJsonAsync<InvoiceDto>())!);
    }

    [Fact]
    public async Task The_pdf_endpoint_returns_a_real_pdf_named_after_the_invoice()
    {
        var (auth, invoice) = await NewInvoiceAsync();

        var response = await _client.GetAsync($"/api/invoices/{invoice.Id}/pdf", auth.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains(invoice.InvoiceNumber, response.Content.Headers.ContentDisposition!.FileName);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 1500);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
    }

    [Fact]
    public async Task Another_tenants_invoice_pdf_is_a_404()
    {
        var (_, invoice) = await NewInvoiceAsync();
        var other = await _client.RegisterAsync();

        var response = await _client.GetAsync($"/api/invoices/{invoice.Id}/pdf", other.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_pdf_endpoint_needs_a_token()
    {
        var response = await _client.GetAsync($"/api/invoices/{Guid.NewGuid()}/pdf");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
