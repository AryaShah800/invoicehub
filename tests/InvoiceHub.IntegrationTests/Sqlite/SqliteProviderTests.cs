using InvoiceHub.Application.Reports;
using InvoiceHub.Domain.Customers;
using InvoiceHub.Domain.Invoices;
using InvoiceHub.Domain.Products;
using InvoiceHub.Domain.Tenants;
using InvoiceHub.Infrastructure.Reports;
using InvoiceHub.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.IntegrationTests.Sqlite;

public sealed class SqliteProviderTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private (Tenant Tenant, Customer Customer, Product P5, Product P12, Product P18) SeedCatalogue(
        string gstin = "27AAPFU0939F1ZV", string state = "27", string customerState = "27")
    {
        var tenant = new Tenant("Demo Traders", gstin, state);
        using (var context = _db.CreateContext(new FakeTenantProvider()))
        {
            context.Tenants.Add(tenant);
            context.SaveChanges();
        }

        using var db = _db.CreateContext(new FakeTenantProvider { TenantId = tenant.Id });
        var customer = new Customer(tenant.Id, "Shah Hardware", "a@shah.example", customerState, new Address("1 Road", null, "Pune", "411001"));
        var p5 = new Product(tenant.Id, "Rice", "1006", 410m, 5m);
        var p12 = new Product(tenant.Id, "Paper", "4802", 320m, 12m);
        var p18 = new Product(tenant.Id, "Stand", "8473", 1499m, 18m);
        db.AddRange(customer, p5, p12, p18);
        db.SaveChanges();
        return (tenant, customer, p5, p12, p18);
    }

    private void AddInvoice(Tenant tenant, Customer customer, string number, DateOnly issued, bool issue, params (Product Product, decimal Qty)[] lines)
    {
        using var db = _db.CreateContext(new FakeTenantProvider { TenantId = tenant.Id });
        var invoice = new Invoice(tenant.Id, tenant.StateCode, customer, number, issued, issued.AddDays(15));
        foreach (var (product, qty) in lines)
            invoice.AddLine(product, qty);
        if (issue)
            invoice.Issue();
        db.Invoices.Add(invoice);
        db.SaveChanges();
    }

    [Fact]
    public void EnsureCreated_seeds_the_identity_roles()
    {
        using var db = _db.CreateContext(new FakeTenantProvider());
        var roles = db.Roles.Select(r => r.Name).OrderBy(n => n).ToList();
        Assert.Equal(["Admin", "Staff"], roles);
    }

    [Fact]
    public void Money_round_trips_exactly()
    {
        var (tenant, customer, _, _, p18) = SeedCatalogue();
        AddInvoice(tenant, customer, "INV-1", new DateOnly(2026, 5, 1), true, (p18, 3m));

        using var db = _db.CreateContext(new FakeTenantProvider { TenantId = tenant.Id });
        var invoice = db.Invoices.Include(i => i.Lines).Single();
        Assert.Equal(4497.00m, invoice.TaxableTotal);
        Assert.Equal(404.73m, invoice.CgstTotal);
        Assert.Equal(404.73m, invoice.SgstTotal);
        Assert.Equal(new DateOnly(2026, 5, 1), invoice.IssueDate);
    }

    [Fact]
    public void A_concurrent_update_of_the_same_invoice_is_rejected()
    {
        var (tenant, customer, _, _, p18) = SeedCatalogue();
        AddInvoice(tenant, customer, "INV-1", new DateOnly(2026, 5, 1), false, (p18, 1m));
        var scope = new FakeTenantProvider { TenantId = tenant.Id };

        using var first = _db.CreateContext(scope);
        using var second = _db.CreateContext(scope);
        var a = first.Invoices.Include(i => i.Lines).Single();
        var b = second.Invoices.Include(i => i.Lines).Single();

        a.SetNotes("edited by first");
        first.SaveChanges();

        b.SetNotes("edited by second");
        Assert.Throws<DbUpdateConcurrencyException>(() => second.SaveChanges());
    }

    [Fact]
    public void Sequential_updates_succeed_because_the_token_is_refreshed_on_every_save()
    {
        var (tenant, customer, _, _, p18) = SeedCatalogue();
        AddInvoice(tenant, customer, "INV-1", new DateOnly(2026, 5, 1), false, (p18, 1m));

        using var db = _db.CreateContext(new FakeTenantProvider { TenantId = tenant.Id });
        var invoice = db.Invoices.Include(i => i.Lines).Single();
        var before = invoice.RowVersion.ToArray();
        invoice.SetNotes("one");
        db.SaveChanges();
        var afterFirst = invoice.RowVersion.ToArray();
        invoice.SetNotes("two");
        db.SaveChanges();

        Assert.NotEmpty(before);
        Assert.NotEqual(before, afterFirst);
        Assert.NotEqual(afterFirst, invoice.RowVersion);
    }

    [Fact]
    public void A_concurrent_update_of_the_same_recurring_invoice_is_rejected()
    {
        var (tenant, customer, _, p12, _) = SeedCatalogue();
        var scope = new FakeTenantProvider { TenantId = tenant.Id };
        using (var db = _db.CreateContext(scope))
        {
            db.RecurringInvoices.Add(new RecurringInvoice(tenant.Id, customer, "Retainer", 1, new DateOnly(2026, 5, 1), 15, null, [(p12, 2m)]));
            db.SaveChanges();
        }

        using var first = _db.CreateContext(scope);
        using var second = _db.CreateContext(scope);
        var a = first.RecurringInvoices.Include(r => r.Lines).Single();
        var b = second.RecurringInvoices.Include(r => r.Lines).Single();
        a.Pause();
        first.SaveChanges();
        b.Advance();
        Assert.Throws<DbUpdateConcurrencyException>(() => second.SaveChanges());
    }

    [Fact]
    public async Task Gst_report_returns_exact_figures_for_the_requested_range_only()
    {
        // seller and customer in state 27: CGST + SGST. Slabs 5, 12, 18.
        var (tenant, customer, p5, p12, p18) = SeedCatalogue();
        var from = new DateOnly(2026, 4, 1);
        var to = new DateOnly(2026, 4, 30);
        AddInvoice(tenant, customer, "A-ON-FROM", from, true, (p5, 1m), (p18, 2m));                   // 5%: 410, 18%: 2998
        AddInvoice(tenant, customer, "B-MID", new DateOnly(2026, 4, 15), true, (p12, 3m), (p18, 1m)); // 12%: 960, 18%: 1499
        AddInvoice(tenant, customer, "C-ON-TO", to, true, (p5, 2m));                                   // 5%: 820
        AddInvoice(tenant, customer, "D-BEFORE", from.AddDays(-1), true, (p5, 100m));                  // outside
        AddInvoice(tenant, customer, "E-AFTER", to.AddDays(1), true, (p5, 100m));                      // outside
        AddInvoice(tenant, customer, "F-DRAFT", new DateOnly(2026, 4, 10), false, (p5, 100m));         // draft: excluded

        // another tenant with an invoice in range must never leak in
        var other = SeedCatalogue("29AAACK5678D1Z2", "29", "29");
        AddInvoice(other.Tenant, other.Customer, "OTHER-1", new DateOnly(2026, 4, 10), true, (other.P5, 50m));

        using var db = _db.CreateContext(new FakeTenantProvider { TenantId = tenant.Id });
        var rows = await new DapperGstReportReader(db).GetSummaryAsync(tenant.Id, from, to, CancellationToken.None);

        Assert.Equal(3, rows.Count);
        Assert.Equal([5m, 12m, 18m], rows.Select(r => r.GstRate).ToArray()); // numeric, not text, order
        AssertRow(rows[0], invoices: 2, taxable: 1230.00m, cgst: 30.75m, sgst: 30.75m, igst: 0m);
        AssertRow(rows[1], invoices: 1, taxable: 960.00m, cgst: 57.60m, sgst: 57.60m, igst: 0m);
        AssertRow(rows[2], invoices: 2, taxable: 4497.00m, cgst: 404.73m, sgst: 404.73m, igst: 0m);
    }

    [Fact]
    public async Task Gst_report_uses_igst_for_other_state_customers()
    {
        var (tenant, customer, p5, _, _) = SeedCatalogue(customerState: "29");
        AddInvoice(tenant, customer, "I-1", new DateOnly(2026, 4, 5), true, (p5, 1m));

        using var db = _db.CreateContext(new FakeTenantProvider { TenantId = tenant.Id });
        var rows = await new DapperGstReportReader(db).GetSummaryAsync(tenant.Id, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), CancellationToken.None);

        var row = Assert.Single(rows);
        AssertRow(row, invoices: 1, taxable: 410.00m, cgst: 0m, sgst: 0m, igst: 20.50m);
    }

    private static void AssertRow(GstSummaryRow row, int invoices, decimal taxable, decimal cgst, decimal sgst, decimal igst)
    {
        Assert.Equal(invoices, row.InvoiceCount);
        Assert.Equal(taxable, row.TaxableValue);
        Assert.Equal(cgst, row.CgstAmount);
        Assert.Equal(sgst, row.SgstAmount);
        Assert.Equal(igst, row.IgstAmount);
    }
}
