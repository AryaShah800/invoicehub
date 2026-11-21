using System.Net;
using System.Net.Http.Json;
using InvoiceHub.Application.Auth;
using InvoiceHub.Application.Common;
using InvoiceHub.Application.Customers;
using InvoiceHub.Application.Invoices.Queries.ListInvoices;
using InvoiceHub.Application.Products;
using InvoiceHub.Domain.Customers;
using InvoiceHub.Domain.Invoices;
using InvoiceHub.Domain.Products;

namespace InvoiceHub.IntegrationTests.Api;

/// <summary>Paged, tenant-filtered list endpoints for customers, products and invoices.</summary>
[Collection(ApiCollection.Name)]
public sealed class ListEndpointsTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private sealed record Seeded(AuthResponse Auth, Customer Acme, Customer Globex, List<Invoice> Invoices);

    private static Customer NewCustomer(Guid tenantId, string name) =>
        new(tenantId, name, TestData.NewEmail(), "27", new Address("1 Main Road", null, "Pune", "411001"));

    /// <summary>
    /// Tenant with two customers and these invoices (issue date, due date, state):
    /// Acme draft 2026-01-10, Acme issued 2026-02-10 due 2026-03-01 (overdue), Acme paid 2026-03-10,
    /// Globex issued 2026-04-10 due 2099 (not overdue).
    /// </summary>
    private async Task<Seeded> SeedAsync()
    {
        var auth = await _client.RegisterAsync();
        await using var db = factory.CreateDbContext(auth.TenantId);
        var acme = NewCustomer(auth.TenantId, "Acme Pvt Ltd");
        var globex = NewCustomer(auth.TenantId, "Globex Corp");
        var product = new Product(auth.TenantId, "Widget", "8471", 1000m, 18m);
        db.AddRange(acme, globex, product);

        Invoice Make(Customer c, string number, DateOnly issue, DateOnly due, Action<Invoice>? then = null)
        {
            var inv = new Invoice(auth.TenantId, "27", c, number, issue, due);
            inv.AddLine(product, 1);
            then?.Invoke(inv);
            return inv;
        }

        var invoices = new List<Invoice>
        {
            Make(acme, "A-1", new(2026, 1, 10), new(2026, 2, 10)),
            Make(acme, "A-2", new(2026, 2, 10), new(2026, 3, 1), i => i.Issue()),
            Make(acme, "A-3", new(2026, 3, 10), new(2026, 4, 10), i =>
            {
                i.Issue();
                i.RecordPayment(Guid.NewGuid().ToString(), i.GrandTotal, null, DateTime.UtcNow);
            }),
            Make(globex, "G-1", new(2026, 4, 10), new(2099, 1, 1), i => i.Issue()),
        };
        db.AddRange(invoices);
        await db.SaveChangesAsync();
        return new Seeded(auth, acme, globex, invoices);
    }

    private async Task<PagedResult<T>> GetPageAsync<T>(string url, string token)
    {
        var response = await _client.GetAsync(url, token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<T>>())!;
    }

    private static string[] Numbers(PagedResult<InvoiceSummaryDto> page) => page.Items.Select(i => i.InvoiceNumber).ToArray();

    // ---- invoices ----

    [Fact]
    public async Task Invoices_are_listed_newest_first_with_customer_name_and_totals()
    {
        var s = await SeedAsync();

        var page = await GetPageAsync<InvoiceSummaryDto>("/api/invoices", s.Auth.AccessToken);

        Assert.Equal(["G-1", "A-3", "A-2", "A-1"], Numbers(page));
        Assert.Equal(4, page.TotalCount);
        Assert.Equal("Globex Corp", page.Items[0].CustomerName);
        Assert.Equal(1180m, page.Items[0].GrandTotal);
        Assert.Equal(0m, page.Items[1].BalanceDue);
    }

    [Fact]
    public async Task Invoices_can_be_filtered_by_status()
    {
        var s = await SeedAsync();

        var page = await GetPageAsync<InvoiceSummaryDto>("/api/invoices?status=Issued", s.Auth.AccessToken);

        Assert.Equal(["G-1", "A-2"], Numbers(page));
    }

    [Fact]
    public async Task Invoices_can_be_filtered_by_customer()
    {
        var s = await SeedAsync();

        var page = await GetPageAsync<InvoiceSummaryDto>($"/api/invoices?customerId={s.Globex.Id}", s.Auth.AccessToken);

        Assert.Equal(["G-1"], Numbers(page));
    }

    [Fact]
    public async Task Invoices_can_be_filtered_by_an_inclusive_issue_date_range()
    {
        var s = await SeedAsync();

        var page = await GetPageAsync<InvoiceSummaryDto>("/api/invoices?from=2026-02-10&to=2026-03-10", s.Auth.AccessToken);

        Assert.Equal(["A-3", "A-2"], Numbers(page));
    }

    [Fact]
    public async Task Overdue_filter_returns_only_open_invoices_past_their_due_date()
    {
        var s = await SeedAsync();

        var overdue = await GetPageAsync<InvoiceSummaryDto>("/api/invoices?overdue=true", s.Auth.AccessToken);
        var notOverdue = await GetPageAsync<InvoiceSummaryDto>("/api/invoices?overdue=false", s.Auth.AccessToken);

        Assert.Equal(["A-2"], Numbers(overdue));
        Assert.True(overdue.Items[0].IsOverdue);
        Assert.Equal(["G-1", "A-3", "A-1"], Numbers(notOverdue));
    }

    [Fact]
    public async Task Filters_combine()
    {
        var s = await SeedAsync();

        var page = await GetPageAsync<InvoiceSummaryDto>(
            $"/api/invoices?customerId={s.Acme.Id}&status=Issued&from=2026-01-01&to=2026-12-31", s.Auth.AccessToken);

        Assert.Equal(["A-2"], Numbers(page));
    }

    [Fact]
    public async Task Invoices_are_paged_and_the_total_ignores_the_page()
    {
        var s = await SeedAsync();

        var first = await GetPageAsync<InvoiceSummaryDto>("/api/invoices?page=1&pageSize=3", s.Auth.AccessToken);
        var second = await GetPageAsync<InvoiceSummaryDto>("/api/invoices?page=2&pageSize=3", s.Auth.AccessToken);

        Assert.Equal(3, first.Items.Count);
        Assert.Equal(["A-1"], Numbers(second));
        Assert.Equal(4, second.TotalCount);
        Assert.Equal(2, second.TotalPages);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("from=2026-05-01&to=2026-04-01")]
    [InlineData("status=Bogus")]
    public async Task Invalid_list_parameters_are_rejected_with_400(string query)
    {
        var auth = await _client.RegisterAsync();

        var response = await _client.GetAsync($"/api/invoices?{query}", auth.AccessToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task One_tenant_never_sees_another_tenants_invoices_even_when_filtering_by_their_customer()
    {
        var mine = await SeedAsync();
        var theirs = await SeedAsync();

        var all = await GetPageAsync<InvoiceSummaryDto>("/api/invoices", mine.Auth.AccessToken);
        var byForeignCustomer = await GetPageAsync<InvoiceSummaryDto>($"/api/invoices?customerId={theirs.Acme.Id}", mine.Auth.AccessToken);

        Assert.Equal(4, all.TotalCount);
        Assert.DoesNotContain(all.Items, i => theirs.Invoices.Any(t => t.Id == i.Id));
        Assert.Empty(byForeignCustomer.Items);
        Assert.Equal(0, byForeignCustomer.TotalCount);
    }

    [Fact]
    public async Task Listing_invoices_requires_authentication()
    {
        var response = await _client.GetAsync("/api/invoices");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- customers ----

    [Fact]
    public async Task Customers_are_listed_by_name_and_can_be_searched()
    {
        var s = await SeedAsync();

        var all = await GetPageAsync<CustomerDto>("/api/customers", s.Auth.AccessToken);
        var found = await GetPageAsync<CustomerDto>("/api/customers?search=glob", s.Auth.AccessToken);

        Assert.Equal(["Acme Pvt Ltd", "Globex Corp"], all.Items.Select(c => c.Name));
        Assert.Equal(["Globex Corp"], found.Items.Select(c => c.Name));
    }

    [Fact]
    public async Task Customers_are_paged_and_tenant_isolated()
    {
        var mine = await SeedAsync();
        await SeedAsync();

        var page = await GetPageAsync<CustomerDto>("/api/customers?page=2&pageSize=1", mine.Auth.AccessToken);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(["Globex Corp"], page.Items.Select(c => c.Name));
        Assert.All(page.Items, c => Assert.Contains(c.Id, new[] { mine.Acme.Id, mine.Globex.Id }));
    }

    [Fact]
    public async Task A_new_tenant_has_an_empty_customer_list()
    {
        await SeedAsync();
        var fresh = await _client.RegisterAsync();

        var page = await GetPageAsync<CustomerDto>("/api/customers", fresh.AccessToken);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    // ---- products ----

    [Fact]
    public async Task Products_can_be_filtered_by_active_flag_and_search_and_are_tenant_isolated()
    {
        var auth = await _client.RegisterAsync();
        var other = await _client.RegisterAsync();
        await using (var db = factory.CreateDbContext(auth.TenantId))
        {
            var inactive = new Product(auth.TenantId, "Old gadget", "8517", 50m, 12m);
            inactive.Deactivate();
            db.AddRange(new Product(auth.TenantId, "Widget", "8471", 1000m, 18m), inactive);
            await db.SaveChangesAsync();
        }
        await using (var db = factory.CreateDbContext(other.TenantId))
        {
            db.Add(new Product(other.TenantId, "Widget", "8471", 1m, 5m));
            await db.SaveChangesAsync();
        }

        var all = await GetPageAsync<ProductDto>("/api/products", auth.AccessToken);
        var active = await GetPageAsync<ProductDto>("/api/products?isActive=true", auth.AccessToken);
        var bySearch = await GetPageAsync<ProductDto>("/api/products?search=8517", auth.AccessToken);

        Assert.Equal(2, all.TotalCount);
        Assert.Equal(["Widget"], active.Items.Select(p => p.Name));
        Assert.Equal(["Old gadget"], bySearch.Items.Select(p => p.Name));
    }
}
