using System.Net;
using System.Net.Http.Json;
using InvoiceHub.Application.Auth;
using InvoiceHub.Application.Common;
using InvoiceHub.Application.Invoices;
using InvoiceHub.Application.Invoices.Queries.ListInvoices;
using InvoiceHub.Application.Reports;
using InvoiceHub.Infrastructure.Demo;
using InvoiceHub.IntegrationTests.Api;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceHub.IntegrationTests.Sqlite;

/// <summary>The hosted-demo configuration end to end: SQLite, EnsureCreated, seeding, reset.</summary>
[Collection(SqliteDemoCollection.Name)]
public sealed class DemoModeTests(SqliteDemoApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<AuthResponse> LoginDemoAsync() => await _client.LoginAsync(factory.Demo.Email, factory.Demo.Password);

    private async Task<List<InvoiceSummaryDto>> AllInvoicesAsync(string token)
    {
        var response = await _client.GetAsync("/api/invoices?pageSize=100", token);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PagedResult<InvoiceSummaryDto>>())!.Items.ToList();
    }

    [Fact]
    public async Task Startup_seeds_the_demo_tenant_with_sixty_invoices_and_four_drafts()
    {
        var auth = await LoginDemoAsync();
        var invoices = await AllInvoicesAsync(auth.AccessToken);

        Assert.Equal(DemoDataSeeder.InvoiceCount, invoices.Count);
        Assert.Equal(DemoDataSeeder.DraftCount, invoices.Count(i => i.Status == "Draft"));
        Assert.Contains(invoices, i => i.Status == "Paid");
        Assert.Contains(invoices, i => i.Status == "PartiallyPaid");
        Assert.Contains(invoices, i => i.Status == "Issued");
        // about eleven months of history
        Assert.InRange((DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - invoices.Min(i => i.IssueDate).DayNumber), 320, 335);
    }

    [Fact]
    public async Task Gst_summary_matches_the_totals_of_the_issued_invoices()
    {
        var auth = await LoginDemoAsync();
        var invoices = await AllInvoicesAsync(auth.AccessToken);
        var billed = invoices.Where(i => i.Status != "Draft").ToList();

        var response = await _client.GetAsync("/api/reports/gst-summary?from=2000-01-01&to=2100-01-01", auth.AccessToken);
        response.EnsureSuccessStatusCode();
        var report = (await response.Content.ReadFromJsonAsync<GstSummaryReport>())!;

        Assert.Equal(billed.Sum(i => i.GrandTotal), report.TaxableValue + report.TotalTax);
        Assert.Equal([12m, 18m], report.Rows.Select(r => r.GstRate).ToArray()); // the invoices use these two slabs
        Assert.True(report.CgstAmount > 0 && report.SgstAmount > 0 && report.IgstAmount > 0); // customers in and out of state
        Assert.Equal(report.CgstAmount, report.SgstAmount);
    }

    [Fact]
    public async Task Invoice_pdf_renders_on_sqlite()
    {
        var auth = await LoginDemoAsync();
        var invoice = (await AllInvoicesAsync(auth.AccessToken)).First(i => i.Status == "Paid");

        var response = await _client.GetAsync($"/api/invoices/{invoice.Id}/pdf", auth.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public async Task The_public_demo_account_cannot_be_locked_out_by_wrong_passwords()
    {
        for (var i = 0; i < 8; i++)
        {
            var bad = await _client.PostAsync("/api/auth/login", new LoginRequest(factory.Demo.Email, "wrong-password-" + i));
            Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        }

        var good = await _client.PostAsync("/api/auth/login", new LoginRequest(factory.Demo.Email, factory.Demo.Password));
        Assert.Equal(HttpStatusCode.OK, good.StatusCode);
    }

    [Fact]
    public async Task Issuing_the_same_draft_twice_at_once_succeeds_only_once()
    {
        // One request wins; the other is refused (422 if it saw the new status, 409 if it hit the concurrency token).
        var auth = await LoginDemoAsync();
        var draft = (await AllInvoicesAsync(auth.AccessToken)).First(i => i.Status == "Draft");
        try
        {
            var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => _client.PostAsync($"/api/invoices/{draft.Id}/issue", new { }, auth.AccessToken)));

            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.OK),
                r => Assert.True(r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity));
        }
        finally
        {
            await factory.Services.GetRequiredService<DemoDataSeeder>().ResetAsync(CancellationToken.None); // keep the shared data pristine
        }
    }

    [Fact]
    public async Task Reset_removes_visitor_data_and_restores_the_demo_data_but_keeps_the_demo_login_valid()
    {
        var demo = await LoginDemoAsync();
        var visitor = await _client.RegisterAsync();
        var created = await _client.PostAsync("/api/customers",
            new { name = "Visitor Co", email = "v@v.example", stateCode = "27", addressLine1 = "1 St", city = "Pune", postalCode = "411001" }, demo.AccessToken);
        created.EnsureSuccessStatusCode();

        await factory.Services.GetRequiredService<DemoDataSeeder>().ResetAsync(CancellationToken.None);

        // the visitor's own tenant and user are gone
        var visitorLogin = await _client.PostAsync("/api/auth/login", new LoginRequest(visitor.Email, TestData.Password));
        Assert.Equal(HttpStatusCode.Unauthorized, visitorLogin.StatusCode);

        // the demo token issued before the reset still works, and the data is the original set again
        var invoices = await AllInvoicesAsync(demo.AccessToken);
        Assert.Equal(DemoDataSeeder.InvoiceCount, invoices.Count);
        var customers = await _client.GetAsync("/api/customers?pageSize=100", demo.AccessToken);
        var page = (await customers.Content.ReadFromJsonAsync<PagedResult<InvoiceHub.Application.Customers.CustomerDto>>())!;
        Assert.Equal(4, page.TotalCount);
        Assert.DoesNotContain(page.Items, c => c.Name == "Visitor Co");
    }

    [Fact]
    public async Task Seeding_twice_does_nothing_the_second_time()
    {
        var seeder = factory.Services.GetRequiredService<DemoDataSeeder>();
        Assert.False(await seeder.SeedIfEmptyAsync(CancellationToken.None));
    }
}
