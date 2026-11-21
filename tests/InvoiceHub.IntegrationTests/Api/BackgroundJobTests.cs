using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using InvoiceHub.Api.Jobs;
using InvoiceHub.Application.Abstractions;
using InvoiceHub.Application.Recurring;
using InvoiceHub.Domain.Customers;
using InvoiceHub.Domain.Invoices;
using InvoiceHub.Domain.Products;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InvoiceHub.IntegrationTests.Api;

/// <summary>Remembers what the job tried to tell tenants, so tests can assert on it.</summary>
public sealed class RecordingNotifier : IInvoiceEventNotifier
{
    public ConcurrentBag<(Guid TenantId, InvoiceOverdueMessage Message)> Overdue { get; } = [];
    public ConcurrentBag<(Guid TenantId, RecurringInvoiceGeneratedMessage Message)> Generated { get; } = [];

    public Task InvoiceOverdueAsync(Guid tenantId, InvoiceOverdueMessage message, CancellationToken ct)
    {
        Overdue.Add((tenantId, message));
        return Task.CompletedTask;
    }

    public Task RecurringInvoiceGeneratedAsync(Guid tenantId, RecurringInvoiceGeneratedMessage message, CancellationToken ct)
    {
        Generated.Add((tenantId, message));
        return Task.CompletedTask;
    }
}

/// <summary>
/// The background job against a real database. Each test seeds its own tenant and runs the worker for a chosen
/// date, so nothing depends on the wall clock. Other tests' tenants in the shared database are processed too,
/// which is why every assertion looks only at this test's rows.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class BackgroundJobTests
{
    private static readonly DateOnly Today = new(2026, 11, 20);

    private readonly ApiFactory _factory;
    private readonly RecordingNotifier _notifier = new();
    private readonly WebApplicationFactory<Program> _host;
    private readonly HttpClient _client;

    public BackgroundJobTests(ApiFactory factory)
    {
        _factory = factory;
        _host = factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.RemoveAll<IInvoiceEventNotifier>();
            services.AddSingleton<IInvoiceEventNotifier>(_notifier);
        }));
        _client = _host.CreateClient();
    }

    private async Task<T?> GetJsonAsync<T>(string token)
    {
        var response = await _client.GetAsync("/api/recurring-invoices", token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>();
    }

    private InvoiceMaintenanceWorker Worker => _host.Services.GetRequiredService<InvoiceMaintenanceWorker>();

    private sealed record Seed(Guid TenantId, string Token, Customer Customer, Product Product);

    private async Task<Seed> SeedTenantAsync()
    {
        var auth = await _client.RegisterAsync();
        await using var db = _factory.CreateDbContext(auth.TenantId);
        var customer = new Customer(auth.TenantId, "Buyer Co", TestData.NewEmail(), "29", new Address("1 Road", null, "Bengaluru", "560001"));
        var product = new Product(auth.TenantId, "Retainer", "9983", 1000m, 18m);
        db.AddRange(customer, product);
        await db.SaveChangesAsync();
        return new Seed(auth.TenantId, auth.AccessToken, customer, product);
    }

    private async Task<Invoice> AddInvoiceAsync(Seed seed, string number, DateOnly due, bool issue = true, decimal? pay = null)
    {
        await using var db = _factory.CreateDbContext(seed.TenantId);
        var customer = await db.Customers.SingleAsync(c => c.Id == seed.Customer.Id);
        var invoice = new Invoice(seed.TenantId, "27", customer, number, due.AddDays(-15), due);
        invoice.AddCustomLine("Work", "9983", 1, 1000m, 18m);
        if (issue)
            invoice.Issue();
        if (pay is { } amount)
            invoice.RecordPayment("pay-" + number, amount, null, DateTime.UtcNow);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice;
    }

    private async Task<DateOnly?> OverdueMarkOf(Seed seed, Guid invoiceId)
    {
        await using var db = _factory.CreateDbContext(seed.TenantId);
        return await db.Invoices.Where(i => i.Id == invoiceId).Select(i => i.OverdueMarkedOn).SingleAsync();
    }

    private async Task<Guid> CreateTemplateAsync(Seed seed, string name, DateOnly first, int months = 1, DateOnly? end = null, Guid? productId = null)
    {
        var response = await _client.PostAsync("/api/recurring-invoices", new
        {
            name,
            customerId = seed.Customer.Id,
            intervalMonths = months,
            firstRunDate = first,
            endDate = end,
            dueInDays = 10,
            lines = new[] { new { productId = productId ?? seed.Product.Id, quantity = 2 } },
        }, seed.Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RecurringInvoiceDto>())!.Id;
    }

    private async Task<List<Invoice>> InvoicesFromAsync(Seed seed, Guid templateId)
    {
        await using var db = _factory.CreateDbContext(seed.TenantId);
        var prefix = "R" + templateId.ToString("N")[..6] + "-";
        return await db.Invoices.Include(i => i.Lines).Where(i => i.InvoiceNumber.StartsWith(prefix)).OrderBy(i => i.IssueDate).ToListAsync();
    }

    [Fact]
    public async Task Overdue_invoices_are_marked_once_and_only_the_right_ones()
    {
        var seed = await SeedTenantAsync();
        var late = await AddInvoiceAsync(seed, "LATE-1", Today.AddDays(-3));
        var partly = await AddInvoiceAsync(seed, "PART-1", Today.AddDays(-1), pay: 100m);
        var paid = await AddInvoiceAsync(seed, "PAID-1", Today.AddDays(-9), pay: 1180m);
        var draft = await AddInvoiceAsync(seed, "DRAFT-1", Today.AddDays(-9), issue: false);
        var notYet = await AddInvoiceAsync(seed, "FUT-1", Today.AddDays(5));
        var dueToday = await AddInvoiceAsync(seed, "TODAY-1", Today);

        await Worker.RunOnceAsync(Today, CancellationToken.None);

        Assert.Equal(Today, await OverdueMarkOf(seed, late.Id));
        Assert.Equal(Today, await OverdueMarkOf(seed, partly.Id));
        foreach (var untouched in new[] { paid, draft, notYet, dueToday })
            Assert.Null(await OverdueMarkOf(seed, untouched.Id));

        var told = _notifier.Overdue.Where(o => o.TenantId == seed.TenantId).Select(o => o.Message.InvoiceNumber).Order().ToList();
        Assert.Equal(["LATE-1", "PART-1"], told);
        Assert.Equal("Buyer Co", _notifier.Overdue.First(o => o.TenantId == seed.TenantId).Message.CustomerName);
        Assert.Equal(1080m, _notifier.Overdue.Single(o => o.Message.InvoiceNumber == "PART-1").Message.BalanceDue);
    }

    [Fact]
    public async Task Running_the_job_again_does_not_mark_or_notify_twice()
    {
        var seed = await SeedTenantAsync();
        var late = await AddInvoiceAsync(seed, "LATE-2", Today.AddDays(-3));

        await Worker.RunOnceAsync(Today, CancellationToken.None);
        await Worker.RunOnceAsync(Today.AddDays(1), CancellationToken.None);
        await Worker.RunOnceAsync(Today.AddDays(2), CancellationToken.None);

        Assert.Equal(Today, await OverdueMarkOf(seed, late.Id));   // still the first day it was noticed
        Assert.Single(_notifier.Overdue, o => o.Message.InvoiceNumber == "LATE-2");
    }

    [Fact]
    public async Task A_payment_before_the_job_runs_keeps_the_invoice_out_of_the_overdue_list()
    {
        var seed = await SeedTenantAsync();
        var invoice = await AddInvoiceAsync(seed, "PAYFIRST-1", Today.AddDays(-2), pay: 1180m);

        await Worker.RunOnceAsync(Today, CancellationToken.None);

        Assert.Null(await OverdueMarkOf(seed, invoice.Id));
        Assert.DoesNotContain(_notifier.Overdue, o => o.Message.InvoiceNumber == "PAYFIRST-1");
    }

    [Fact]
    public async Task A_monthly_template_catches_up_missed_months_then_does_nothing_on_a_rerun()
    {
        var seed = await SeedTenantAsync();
        var templateId = await CreateTemplateAsync(seed, "Retainer", new DateOnly(2026, 9, 15));

        await Worker.RunOnceAsync(Today, CancellationToken.None);   // 15 Sep, 15 Oct, 15 Nov are due
        var first = await InvoicesFromAsync(seed, templateId);
        await Worker.RunOnceAsync(Today, CancellationToken.None);
        await Worker.RunOnceAsync(Today.AddDays(5), CancellationToken.None);
        var second = await InvoicesFromAsync(seed, templateId);

        Assert.Equal([new DateOnly(2026, 9, 15), new DateOnly(2026, 10, 15), new DateOnly(2026, 11, 15)], first.Select(i => i.IssueDate));
        Assert.Equal(first.Select(i => i.Id), second.Select(i => i.Id));
        Assert.All(first, i =>
        {
            Assert.Equal(InvoiceStatus.Issued, i.Status);
            Assert.Equal(i.IssueDate.AddDays(10), i.DueDate);
            Assert.Equal(2360m, i.GrandTotal);   // 2 x 1000 + 18% IGST (buyer in another state)
            Assert.Equal(seed.TenantId, i.TenantId);
        });
        Assert.Equal(3, _notifier.Generated.Count(g => g.Message.RecurringInvoiceId == templateId));

        var next = (await GetJsonAsync<List<RecurringInvoiceDto>>(seed.Token))!.Single(t => t.Id == templateId);
        Assert.Equal(new DateOnly(2026, 12, 15), next.NextRunDate);
    }

    [Fact]
    public async Task The_next_occurrence_is_generated_when_its_date_arrives()
    {
        var seed = await SeedTenantAsync();
        var templateId = await CreateTemplateAsync(seed, "Retainer", new DateOnly(2026, 11, 15));

        await Worker.RunOnceAsync(Today, CancellationToken.None);
        await Worker.RunOnceAsync(new DateOnly(2026, 12, 14), CancellationToken.None);
        Assert.Single(await InvoicesFromAsync(seed, templateId));

        await Worker.RunOnceAsync(new DateOnly(2026, 12, 15), CancellationToken.None);
        Assert.Equal(2, (await InvoicesFromAsync(seed, templateId)).Count);
    }

    [Fact]
    public async Task A_template_stops_at_its_end_date_and_a_paused_template_generates_nothing()
    {
        var seed = await SeedTenantAsync();
        var ending = await CreateTemplateAsync(seed, "Ends", new DateOnly(2026, 9, 15), end: new DateOnly(2026, 10, 20));
        var paused = await CreateTemplateAsync(seed, "Paused", new DateOnly(2026, 9, 15));
        var pause = await _client.PostAsync($"/api/recurring-invoices/{paused}/pause", new { }, seed.Token);
        Assert.Equal(HttpStatusCode.OK, pause.StatusCode);

        await Worker.RunOnceAsync(Today, CancellationToken.None);

        Assert.Equal(2, (await InvoicesFromAsync(seed, ending)).Count);   // 15 Sep and 15 Oct, not 15 Nov
        Assert.Empty(await InvoicesFromAsync(seed, paused));
    }

    [Fact]
    public async Task An_occurrence_that_already_exists_is_not_created_twice()
    {
        // Simulates a crash after the invoice was saved but before the template moved on.
        var seed = await SeedTenantAsync();
        var templateId = await CreateTemplateAsync(seed, "Retainer", new DateOnly(2026, 11, 15));
        await using (var db = _factory.CreateDbContext(seed.TenantId))
        {
            var template = await db.RecurringInvoices.SingleAsync(r => r.Id == templateId);
            var customer = await db.Customers.SingleAsync(c => c.Id == seed.Customer.Id);
            var existing = new Invoice(seed.TenantId, "27", customer, template.NumberFor(new DateOnly(2026, 11, 15)),
                new DateOnly(2026, 11, 15), new DateOnly(2026, 11, 25));
            existing.AddCustomLine("Work", "9983", 1, 1m, 0m);
            existing.Issue();
            db.Invoices.Add(existing);
            await db.SaveChangesAsync();
        }

        await Worker.RunOnceAsync(Today, CancellationToken.None);

        Assert.Single(await InvoicesFromAsync(seed, templateId));
        var next = (await GetJsonAsync<List<RecurringInvoiceDto>>(seed.Token))!.Single(t => t.Id == templateId);
        Assert.Equal(new DateOnly(2026, 12, 15), next.NextRunDate);
    }

    [Fact]
    public async Task A_broken_template_does_not_stop_the_others_or_other_tenants()
    {
        var seed = await SeedTenantAsync();
        var other = await SeedTenantAsync();
        var retiredProduct = new Product(seed.TenantId, "Retired", "9983", 10m, 5m);
        await using (var db = _factory.CreateDbContext(seed.TenantId))
        {
            db.Products.Add(retiredProduct);
            await db.SaveChangesAsync();
        }
        var broken = await CreateTemplateAsync(seed, "Broken", new DateOnly(2026, 11, 1), productId: retiredProduct.Id);
        var healthy = await CreateTemplateAsync(seed, "Healthy", new DateOnly(2026, 11, 2));
        var othersTemplate = await CreateTemplateAsync(other, "Other", new DateOnly(2026, 11, 3));
        await using (var db = _factory.CreateDbContext(seed.TenantId))
        {
            (await db.Products.SingleAsync(p => p.Id == retiredProduct.Id)).Deactivate();
            await db.SaveChangesAsync();
        }

        await Worker.RunOnceAsync(Today, CancellationToken.None);

        Assert.Empty(await InvoicesFromAsync(seed, broken));
        Assert.Single(await InvoicesFromAsync(seed, healthy));
        Assert.Single(await InvoicesFromAsync(other, othersTemplate));
    }

    [Fact]
    public async Task Templates_are_private_to_their_tenant()
    {
        var mine = await SeedTenantAsync();
        var theirs = await SeedTenantAsync();
        var theirTemplate = await CreateTemplateAsync(theirs, "Theirs", new DateOnly(2026, 11, 1));

        var list = await GetJsonAsync<List<RecurringInvoiceDto>>(mine.Token);
        var pause = await _client.PostAsync($"/api/recurring-invoices/{theirTemplate}/pause", new { }, mine.Token);
        var create = await _client.PostAsync("/api/recurring-invoices", new
        {
            name = "Steal", customerId = theirs.Customer.Id, intervalMonths = 1, firstRunDate = Today, dueInDays = 5,
            lines = new[] { new { productId = mine.Product.Id, quantity = 1 } },
        }, mine.Token);

        Assert.Empty(list!);
        Assert.Equal(HttpStatusCode.NotFound, pause.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, create.StatusCode);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(13, 1)]
    [InlineData(1, 0)]
    public async Task Invalid_templates_are_rejected_with_400(int months, int quantity)
    {
        var seed = await SeedTenantAsync();

        var response = await _client.PostAsync("/api/recurring-invoices", new
        {
            name = "Bad", customerId = seed.Customer.Id, intervalMonths = months, firstRunDate = Today, dueInDays = 5,
            lines = new[] { new { productId = seed.Product.Id, quantity } },
        }, seed.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_recurring_endpoints_need_a_signed_in_user()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/recurring-invoices")).StatusCode);
    }
}
