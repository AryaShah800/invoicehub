using System.Diagnostics;
using InvoiceHub.Domain.Customers;
using InvoiceHub.Domain.Invoices;
using InvoiceHub.Domain.Products;
using InvoiceHub.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Benchmarks;

/// <summary>
/// Fills the benchmark database with synthetic invoices for one tenant, built through the real domain
/// classes so the stored tax amounts follow the same rules as the API. The data is random but seeded,
/// so every run produces the same invoices.
/// </summary>
internal static class Seeder
{
    public const string TenantName = "Benchmark Traders";
    private const string TenantGstin = "27ABCDE1234F1Z5";
    private const string SellerState = "27";
    private const int BatchSize = 2000;

    private static readonly DateOnly FirstDay = new(2025, 1, 1);
    private const int DaySpan = 669; // 2025-01-01 to 2026-10-31

    public static async Task<Guid> EnsureSeededAsync(int invoiceTarget)
    {
        await using (var migrate = BenchDatabase.CreateContext(null))
            await migrate.Database.MigrateAsync();

        Guid tenantId;
        await using (var db = BenchDatabase.CreateContext(null))
        {
            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Name == TenantName);
            if (tenant is null)
            {
                tenant = new Tenant(TenantName, TenantGstin, SellerState);
                db.Tenants.Add(tenant);
                await db.SaveChangesAsync();
            }
            tenantId = tenant.Id;
        }

        await using var scoped = BenchDatabase.CreateContext(tenantId);
        var existing = await scoped.Invoices.CountAsync();
        if (existing >= invoiceTarget)
        {
            Console.WriteLine($"Already seeded: {existing:N0} invoices. Nothing to do.");
            return tenantId;
        }

        var random = new Random(42);
        var customers = await EnsureCustomersAsync(scoped, tenantId);
        var products = await EnsureProductsAsync(scoped, tenantId);
        await scoped.DisposeAsync();

        var clock = Stopwatch.StartNew();
        var created = existing;
        while (created < invoiceTarget)
        {
            var count = Math.Min(BatchSize, invoiceTarget - created);
            await using var db = BenchDatabase.CreateContext(tenantId);
            for (var i = 0; i < count; i++)
                db.Invoices.Add(NewInvoice(tenantId, created + i, random, customers, products));
            await db.SaveChangesAsync();
            created += count;
            Console.WriteLine($"  {created:N0} / {invoiceTarget:N0} invoices ({clock.Elapsed:mm\\:ss})");
        }

        Console.WriteLine($"Seeded {created - existing:N0} invoices in {clock.Elapsed:mm\\:ss}.");
        return tenantId;
    }

    private static Invoice NewInvoice(Guid tenantId, int index, Random random, List<Customer> customers, List<Product> products)
    {
        var customer = customers[random.Next(customers.Count)];
        var issued = FirstDay.AddDays(random.Next(DaySpan));
        var invoice = new Invoice(tenantId, SellerState, customer, $"BN-{index + 1:D7}", issued, issued.AddDays(30));

        var lines = random.Next(1, 6); // 1 to 5 lines
        for (var l = 0; l < lines; l++)
            invoice.AddLine(products[random.Next(products.Count)], random.Next(1, 20));

        // About 90% issued, 5% left as drafts, 5% cancelled. The report only counts the issued ones.
        var roll = random.Next(100);
        if (roll >= 5)
            invoice.Issue();
        if (roll >= 95)
            invoice.Cancel();
        return invoice;
    }

    private static async Task<List<Customer>> EnsureCustomersAsync(DbContext db, Guid tenantId)
    {
        var existing = await db.Set<Customer>().AsNoTracking().ToListAsync();
        if (existing.Count > 0)
            return existing;

        // Mostly the seller's own state, so both CGST/SGST and IGST show up in the data.
        string[] states = ["27", "27", "27", "24", "29", "07"];
        var customers = Enumerable.Range(1, 200)
            .Select(n => new Customer(tenantId, $"Customer {n}", $"customer{n}@bench.test", states[n % states.Length],
                new Address($"{n} Market Road", null, "Town", "380001")))
            .ToList();
        db.AddRange(customers);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return customers;
    }

    private static async Task<List<Product>> EnsureProductsAsync(DbContext db, Guid tenantId)
    {
        var existing = await db.Set<Product>().AsNoTracking().ToListAsync();
        if (existing.Count > 0)
            return existing;

        decimal[] rates = [5m, 12m, 18m, 18m, 28m];
        var products = Enumerable.Range(1, 50)
            .Select(n => new Product(tenantId, $"Product {n}", "8471", 49.99m + n * 13.37m, rates[n % rates.Length]))
            .ToList();
        db.AddRange(products);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return products;
    }
}
