using System.Net;
using System.Net.Http.Json;
using InvoiceHub.Application.Abstractions;
using InvoiceHub.Application.Dashboard;
using InvoiceHub.Domain.Customers;
using InvoiceHub.Domain.Invoices;
using InvoiceHub.Domain.Products;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceHub.IntegrationTests.Api;

[Collection(ApiCollection.Name)]
public sealed class DashboardTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private DateOnly Today => factory.Services.GetRequiredService<IBusinessClock>().Today();

    [Fact]
    public async Task Dashboard_sums_billed_value_by_month_and_counts_overdue()
    {
        var auth = await _client.RegisterAsync();
        await using (var db = factory.CreateDbContext(auth.TenantId))
        {
            // Buyer in state 29, seller 27: interstate, so IGST only.
            var buyer = new Customer(auth.TenantId, "Buyer", TestData.NewEmail(), "29", new Address("1 Road", null, "Bengaluru", "560001"));
            var product = new Product(auth.TenantId, "Widget", "8471", 1000m, 18m);
            db.AddRange(buyer, product);

            Invoice Make(string no, DateOnly issue, DateOnly due, bool issued)
            {
                var inv = new Invoice(auth.TenantId, "27", buyer, no, issue, due);
                inv.AddLine(product, 1);
                if (issued) inv.Issue();
                return inv;
            }
            var thisMonth = new DateOnly(Today.Year, Today.Month, 1);
            db.AddRange(
                // Due a week from today, so it is never overdue. "thisMonth + 20 days" made this test
                // fail after the 21st of every month.
                Make("D-1", thisMonth, Today.AddDays(7), true),                                    // billed, not overdue
                Make("D-2", thisMonth.AddMonths(-1), thisMonth.AddMonths(-1).AddDays(5), true),    // billed, overdue
                Make("D-3", thisMonth, thisMonth.AddDays(20), false));                             // draft: excluded
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync("/api/dashboard?months=3", auth.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dash = (await response.Content.ReadFromJsonAsync<DashboardSummary>())!;
        Assert.Equal(3, dash.Months.Count);
        Assert.Equal(1180m, dash.Months[2].Total);          // current month, draft excluded
        Assert.Equal(1180m, dash.Months[1].Total);
        Assert.Equal(0m, dash.Months[0].Total);
        Assert.Equal(360m, dash.Igst);
        Assert.Equal(0m, dash.Cgst);
        Assert.Equal(1, dash.DraftCount);
        Assert.Equal(1, dash.OverdueCount);
        Assert.Equal(1180m, dash.OverdueAmount);
        Assert.Equal(2360m, dash.OutstandingAmount);
    }

    [Fact]
    public async Task Dashboard_only_counts_the_callers_tenant()
    {
        var mine = await _client.RegisterAsync();
        var other = await _client.RegisterAsync();
        await using (var db = factory.CreateDbContext(other.TenantId))
        {
            var buyer = new Customer(other.TenantId, "Buyer", TestData.NewEmail(), "27", new Address("1 Road", null, "Pune", "411001"));
            var product = new Product(other.TenantId, "Widget", "8471", 1000m, 18m);
            var inv = new Invoice(other.TenantId, "27", buyer, "X-1", Today, Today.AddDays(5));
            inv.AddLine(product, 1);
            inv.Issue();
            db.AddRange(buyer, product, inv);
            await db.SaveChangesAsync();
        }

        var dash = (await (await _client.GetAsync("/api/dashboard", mine.AccessToken)).Content.ReadFromJsonAsync<DashboardSummary>())!;

        Assert.All(dash.Months, m => Assert.Equal(0m, m.Total));
        Assert.Equal(0m, dash.OutstandingAmount);
    }

    [Fact]
    public async Task Dashboard_requires_authentication()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/dashboard")).StatusCode);
    }
}
