using InvoiceHub.Domain.Common;
using InvoiceHub.Domain.Customers;
using InvoiceHub.Domain.Invoices;
using InvoiceHub.Domain.Products;
using InvoiceHub.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.IntegrationTests.Persistence;

public sealed class TenantIsolationTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly Tenant _acme = new("Acme Traders", "27AAPFU0939F1ZV", "27");
    private readonly Tenant _globex = new("Globex Exports", "24AAACG1234F1Z5", "24");

    public TenantIsolationTests()
    {
        // Tenants are not tenant-owned, so they can be created without a current tenant.
        using var setup = _db.CreateContext(new FakeTenantProvider());
        setup.Tenants.AddRange(_acme, _globex);
        setup.SaveChanges();

        SeedAs(_acme, "Acme Customer", "Acme Widget", "ACME-001");
        SeedAs(_globex, "Globex Customer", "Globex Gadget", "GLOBEX-001");
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Customers_are_limited_to_the_current_tenant()
    {
        await using var context = _db.CreateContext(new FakeTenantProvider { TenantId = _acme.Id });

        var names = await context.Customers.Select(c => c.Name).ToListAsync();

        Assert.Equal(["Acme Customer"], names);
    }

    [Fact]
    public async Task Products_are_limited_to_the_current_tenant()
    {
        await using var context = _db.CreateContext(new FakeTenantProvider { TenantId = _globex.Id });

        var names = await context.Products.Select(p => p.Name).ToListAsync();

        Assert.Equal(["Globex Gadget"], names);
    }

    [Fact]
    public async Task Invoices_and_their_lines_are_limited_to_the_current_tenant()
    {
        await using var context = _db.CreateContext(new FakeTenantProvider { TenantId = _acme.Id });

        var invoices = await context.Invoices.Include(i => i.Lines).ToListAsync();

        var invoice = Assert.Single(invoices);
        Assert.Equal("ACME-001", invoice.InvoiceNumber);
        Assert.Single(invoice.Lines);
    }

    [Fact]
    public async Task Invoice_lines_queried_directly_are_limited_to_the_current_tenant()
    {
        await using var context = _db.CreateContext(new FakeTenantProvider { TenantId = _globex.Id });

        var lines = await context.InvoiceLines.ToListAsync();

        var line = Assert.Single(lines);
        Assert.Equal("Globex Gadget", line.Description);
    }

    [Fact]
    public async Task Another_tenants_invoice_cannot_be_loaded_by_id()
    {
        Guid globexInvoiceId;
        await using (var globex = _db.CreateContext(new FakeTenantProvider { TenantId = _globex.Id }))
            globexInvoiceId = (await globex.Invoices.SingleAsync()).Id;

        await using var acme = _db.CreateContext(new FakeTenantProvider { TenantId = _acme.Id });

        Assert.Null(await acme.Invoices.FirstOrDefaultAsync(i => i.Id == globexInvoiceId));
    }

    [Fact]
    public async Task No_current_tenant_means_no_tenant_data_is_visible()
    {
        await using var context = _db.CreateContext(new FakeTenantProvider { TenantId = null });

        Assert.Empty(await context.Customers.ToListAsync());
        Assert.Empty(await context.Products.ToListAsync());
        Assert.Empty(await context.Invoices.ToListAsync());
    }

    [Fact]
    public async Task IgnoreQueryFilters_still_exposes_all_tenants_for_deliberate_admin_use()
    {
        await using var context = _db.CreateContext(new FakeTenantProvider { TenantId = _acme.Id });

        Assert.Equal(2, await context.Customers.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Saving_data_for_a_different_tenant_is_rejected()
    {
        await using var context = _db.CreateContext(new FakeTenantProvider { TenantId = _acme.Id });
        context.Products.Add(new Product(_globex.Id, "Smuggled item", "1234", 10m, 18m));

        await Assert.ThrowsAsync<DomainException>(() => context.SaveChangesAsync());
    }

    private void SeedAs(Tenant tenant, string customerName, string productName, string invoiceNumber)
    {
        using var context = _db.CreateContext(new FakeTenantProvider { TenantId = tenant.Id });

        var customer = new Customer(tenant.Id, customerName, $"{tenant.StateCode}@example.com", tenant.StateCode,
            new Address("1 Main Road", null, "Town", "380001"), gstin: null);
        var product = new Product(tenant.Id, productName, "9983", 1000m, 18m);
        var invoice = new Invoice(tenant.Id, customer, invoiceNumber,
            new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));
        invoice.AddLine(product, 2);

        context.AddRange(customer, product, invoice);
        context.SaveChanges();
    }
}
