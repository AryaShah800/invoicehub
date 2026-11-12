using System.Net;
using System.Net.Http.Json;
using InvoiceHub.Application.Auth;
using InvoiceHub.Application.Reports;
using InvoiceHub.Domain.Customers;
using InvoiceHub.Domain.Invoices;
using InvoiceHub.Infrastructure.Reports;

namespace InvoiceHub.IntegrationTests.Api;

/// <summary>
/// The GST summary stored procedure (through Dapper), checked against hand-worked numbers
/// and against the equivalent EF Core query.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class GstReportTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly DateOnly From = new(2026, 11, 1);
    private static readonly DateOnly To = new(2026, 11, 30);

    /// <summary>
    /// Seller is in state 27. Invoices in range that count: A (27 to 27) and B (27 to 29).
    /// Not counted: C draft, D cancelled, E issued after the range.
    /// </summary>
    private async Task<AuthResponse> SeedAsync()
    {
        var auth = await _client.RegisterAsync();
        await SeedInvoicesAsync(auth.TenantId);
        return auth;
    }

    private async Task SeedInvoicesAsync(Guid tenantId)
    {
        await using var db = factory.CreateDbContext(tenantId);
        var local = NewCustomer(tenantId, "27");
        var remote = NewCustomer(tenantId, "29");
        db.AddRange(local, remote);

        var a = NewInvoice(tenantId, local, "A", new DateOnly(2026, 11, 5));
        a.AddCustomLine("Consulting", "9983", 1, 1000m, 18m); // CGST 90 + SGST 90
        a.AddCustomLine("Books", "4901", 1, 200m, 5m);        // CGST 5 + SGST 5

        var b = NewInvoice(tenantId, remote, "B", new DateOnly(2026, 11, 20));
        b.AddCustomLine("Consulting", "9983", 1, 500m, 18m);  // IGST 90

        var c = NewInvoice(tenantId, local, "C", new DateOnly(2026, 11, 10));
        c.AddCustomLine("Draft work", "9983", 1, 9999m, 18m);

        var d = NewInvoice(tenantId, local, "D", new DateOnly(2026, 11, 12));
        d.AddCustomLine("Cancelled work", "9983", 1, 8888m, 18m);

        var e = NewInvoice(tenantId, local, "E", new DateOnly(2026, 12, 5));
        e.AddCustomLine("Next month", "9983", 1, 7777m, 18m);

        a.Issue();
        b.Issue();
        d.Issue();
        d.Cancel();
        e.Issue();

        db.AddRange(a, b, c, d, e);
        await db.SaveChangesAsync();
    }

    private static Customer NewCustomer(Guid tenantId, string state) =>
        new(tenantId, $"Buyer {state}", TestData.NewEmail(), state, new Address("1 Road", null, "Town", "380001"));

    private static Invoice NewInvoice(Guid tenantId, Customer customer, string number, DateOnly issued) =>
        new(tenantId, "27", customer, $"{number}-{Guid.NewGuid():N}"[..16], issued, issued.AddDays(15));

    private async Task<GstSummaryReport> GetReportAsync(AuthResponse auth, DateOnly from, DateOnly to)
    {
        var response = await _client.GetAsync($"/api/reports/gst-summary?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", auth.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GstSummaryReport>())!;
    }

    [Fact]
    public async Task Report_matches_hand_worked_numbers_and_leaves_out_draft_cancelled_and_out_of_range_invoices()
    {
        var auth = await SeedAsync();

        var report = await GetReportAsync(auth, From, To);

        Assert.Equal(2, report.Rows.Count);
        var five = report.Rows[0];
        Assert.Equal((5m, 1, 200m, 5m, 5m, 0m), (five.GstRate, five.InvoiceCount, five.TaxableValue, five.CgstAmount, five.SgstAmount, five.IgstAmount));
        var eighteen = report.Rows[1];
        Assert.Equal((18m, 2, 1500m, 90m, 90m, 90m), (eighteen.GstRate, eighteen.InvoiceCount, eighteen.TaxableValue, eighteen.CgstAmount, eighteen.SgstAmount, eighteen.IgstAmount));
        Assert.Equal(1700m, report.TaxableValue);
        Assert.Equal(95m, report.CgstAmount);
        Assert.Equal(95m, report.SgstAmount);
        Assert.Equal(90m, report.IgstAmount);
        Assert.Equal(280m, report.TotalTax);
    }

    [Fact]
    public async Task Dapper_stored_procedure_and_EF_query_return_the_same_rows()
    {
        var auth = await SeedAsync();

        await using var dapperContext = factory.CreateDbContext(auth.TenantId);
        await using var efContext = factory.CreateDbContext(auth.TenantId);
        var viaProc = await new DapperGstReportReader(dapperContext).GetSummaryAsync(auth.TenantId, From, To, default);
        var viaEf = await new EfGstReportReader(efContext).GetSummaryAsync(auth.TenantId, From, To, default);

        Assert.Equal(viaEf, viaProc);
        Assert.NotEmpty(viaProc);
    }

    [Fact]
    public async Task The_report_only_covers_the_callers_own_tenant()
    {
        var mine = await SeedAsync();
        var other = await _client.RegisterAsync();
        await SeedInvoicesAsync(other.TenantId);

        // The procedure bypasses EF filters, so check it directly with each tenant id.
        await using var ctx = factory.CreateDbContext(mine.TenantId);
        var dapper = new DapperGstReportReader(ctx);
        var mineRows = await dapper.GetSummaryAsync(mine.TenantId, From, To, default);
        var unknownTenant = await dapper.GetSummaryAsync(Guid.NewGuid(), From, To, default);

        Assert.Equal(2, mineRows.Count);
        Assert.Equal(1700m, mineRows.Sum(r => r.TaxableValue)); // not 3400: the other tenant's invoices are excluded
        Assert.Empty(unknownTenant);
        Assert.Equal(1700m, (await GetReportAsync(mine, From, To)).TaxableValue);
    }

    [Fact]
    public async Task A_range_with_no_invoices_gives_an_empty_report()
    {
        var auth = await SeedAsync();

        var report = await GetReportAsync(auth, new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 31));

        Assert.Empty(report.Rows);
        Assert.Equal(0m, report.TotalTax);
    }

    [Fact]
    public async Task The_date_range_includes_both_end_dates()
    {
        var auth = await SeedAsync();

        var onlyA = await GetReportAsync(auth, new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 5));

        Assert.Equal(1200m, onlyA.TaxableValue);
    }

    [Fact]
    public async Task To_before_from_is_a_400()
    {
        var auth = await _client.RegisterAsync();

        var response = await _client.GetAsync("/api/reports/gst-summary?from=2026-11-30&to=2026-11-01", auth.AccessToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Staff_users_cannot_see_the_report()
    {
        var admin = await _client.RegisterAsync();
        var email = TestData.NewEmail();
        (await _client.PostAsync("/api/users", new CreateStaffUserRequest(email, TestData.Password), admin.AccessToken)).EnsureSuccessStatusCode();
        var staff = await _client.LoginAsync(email, TestData.Password);

        var response = await _client.GetAsync("/api/reports/gst-summary?from=2026-11-01&to=2026-11-30", staff.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_report_needs_a_token()
    {
        var response = await _client.GetAsync("/api/reports/gst-summary?from=2026-11-01&to=2026-11-30");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
