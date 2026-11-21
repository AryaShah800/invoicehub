using InvoiceHub.Application.Abstractions;
using InvoiceHub.Application.Auth;
using InvoiceHub.Application.Customers.Commands;
using InvoiceHub.Application.Invoices.Commands.CreateInvoice;
using InvoiceHub.Application.Invoices.Commands.IssueInvoice;
using InvoiceHub.Application.Payments;
using InvoiceHub.Application.Products.Commands;
using InvoiceHub.Domain.Invoices;
using InvoiceHub.Infrastructure.Identity;
using InvoiceHub.Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvoiceHub.Infrastructure.Demo;

public sealed class DemoOptions
{
    public const string SectionName = "Demo";

    /// <summary>Off by default. When on, the demo tenant is created if missing and reset on a timer.</summary>
    public bool Enabled { get; set; }

    /// <summary>Public demo credentials: shown on the login page and in the README, never a real account.</summary>
    public string Email { get; set; } = "demo@invoicehub.example";

    public string Password { get; set; } = "Demo#Pass2026";

    public string BusinessName { get; set; } = "Demo Traders Pvt Ltd";
    public string Gstin { get; set; } = "27AAPFU0939F1ZV";
    public string StateCode { get; set; } = "27";

    /// <summary>How often visitors' changes are wiped and the demo data recreated.</summary>
    public double ResetIntervalHours { get; set; } = 6;
}

/// <summary>
/// Creates the demo tenant and the same data docker/seed.sh creates through the API: 4 customers (one in the
/// seller's state, three outside it), 5 products and 60 invoices over about 11 months, most of the old ones
/// paid. It goes through the real commands, so every business rule applies, and the newest invoices stay drafts.
/// </summary>
public sealed class DemoDataSeeder(
    IServiceScopeFactory scopes,
    IOptions<DemoOptions> options,
    TimeProvider clock,
    ILogger<DemoDataSeeder> logger)
{
    public const int InvoiceCount = 60;
    public const int DraftCount = 4;

    private DemoOptions Options => options.Value;

    /// <summary>Seeds only when the demo account does not exist yet. Returns true when it seeded.</summary>
    public async Task<bool> SeedIfEmptyAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await users.FindByEmailAsync(Options.Email.ToLowerInvariant()) is not null)
            return false;

        var tenantId = await RegisterDemoAccountAsync(scope.ServiceProvider, ct);
        await SeedBusinessDataAsync(tenantId, ct);
        logger.LogInformation("Seeded the demo tenant {TenantId} with {Count} invoices", tenantId, InvoiceCount);
        return true;
    }

    /// <summary>
    /// Wipes everything visitors may have created and recreates the demo data. The demo tenant and user rows are
    /// kept, so a signed-in visitor's token stays valid across a reset.
    /// </summary>
    public async Task ResetAsync(CancellationToken ct)
    {
        Guid tenantId;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InvoiceHubDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var demoUser = await users.FindByEmailAsync(Options.Email.ToLowerInvariant());
            if (demoUser is null)
            {
                await SeedIfEmptyAsync(ct);
                return;
            }

            tenantId = demoUser.TenantId;
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            // Children before parents. Query filters are bypassed on purpose: this clears every tenant's data.
            await db.Payments.IgnoreQueryFilters().ExecuteDeleteAsync(ct);
            await db.InvoiceLines.IgnoreQueryFilters().ExecuteDeleteAsync(ct);
            await db.Invoices.IgnoreQueryFilters().ExecuteDeleteAsync(ct);
            await db.Set<RecurringInvoiceLine>().ExecuteDeleteAsync(ct);
            await db.RecurringInvoices.IgnoreQueryFilters().ExecuteDeleteAsync(ct);
            await db.Products.IgnoreQueryFilters().ExecuteDeleteAsync(ct);
            await db.Customers.IgnoreQueryFilters().ExecuteDeleteAsync(ct);

            // Staff users and tenants that visitors registered.
            await db.UserRoles.Where(r => r.UserId != demoUser.Id).ExecuteDeleteAsync(ct);
            await db.UserClaims.Where(c => c.UserId != demoUser.Id).ExecuteDeleteAsync(ct);
            await db.UserLogins.Where(l => l.UserId != demoUser.Id).ExecuteDeleteAsync(ct);
            await db.UserTokens.Where(t => t.UserId != demoUser.Id).ExecuteDeleteAsync(ct);
            await db.Users.Where(u => u.Id != demoUser.Id).ExecuteDeleteAsync(ct);
            await db.Tenants.Where(t => t.Id != tenantId).ExecuteDeleteAsync(ct);
            await transaction.CommitAsync(ct);

            await KeepAccountUsableAsync(users, demoUser);
        }

        await SeedBusinessDataAsync(tenantId, ct);
        logger.LogInformation("Reset the demo tenant {TenantId}", tenantId);
    }

    private async Task<Guid> RegisterDemoAccountAsync(IServiceProvider services, CancellationToken ct)
    {
        var auth = services.GetRequiredService<IAuthService>();
        var result = await auth.RegisterTenantAsync(
            new RegisterTenantRequest(Options.BusinessName, Options.Gstin, Options.StateCode, Options.Email, Options.Password), ct);
        if (!result.Succeeded)
            throw new InvalidOperationException("Could not create the demo account: " + string.Join(" ", result.Errors));

        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(result.Value!.UserId.ToString())
            ?? throw new InvalidOperationException("The demo user was not found after it was created.");
        await KeepAccountUsableAsync(users, user);
        return result.Value.TenantId;
    }

    /// <summary>
    /// The credentials are public, so anyone can type a wrong password five times. With lockout on that would
    /// lock every visitor out of the demo for 15 minutes, so the demo account never locks.
    /// </summary>
    private static async Task KeepAccountUsableAsync(UserManager<ApplicationUser> users, ApplicationUser user)
    {
        await users.SetLockoutEnabledAsync(user, false);
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
    }

    private async Task SeedBusinessDataAsync(Guid tenantId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        // No HTTP request here, so the tenant is assigned up front; the filters and write guard stay in force.
        scope.ServiceProvider.GetRequiredService<ITenantAssigner>().Assign(tenantId);
        var mediator = scope.ServiceProvider.GetRequiredService<ISender>();

        // state 27 is the seller's own state (CGST + SGST); 29, 07 and 24 are other states (IGST)
        var customers = new[]
        {
            await mediator.Send(new CreateCustomerCommand("Shah Hardware", "accounts@shah.example", "27BBBPS1234C1Z5", "27", "12 MG Road", null, "Pune", "411001"), ct),
            await mediator.Send(new CreateCustomerCommand("Kaveri Electronics", "buy@kaveri.example", "29AAACK5678D1Z2", "29", "88 Brigade Road", null, "Bengaluru", "560001"), ct),
            await mediator.Send(new CreateCustomerCommand("Delhi Office Supplies", "po@dos.example", "07AAACD4321E1Z9", "07", "5 Connaught Place", null, "New Delhi", "110001"), ct),
            await mediator.Send(new CreateCustomerCommand("Gujarat Stationers", "hello@gujstat.example", null, "24", "3 Ashram Road", null, "Ahmedabad", "380009"), ct),
        };
        var laptopStand = await mediator.Send(new CreateProductCommand("Laptop stand", "8473", 1499m, 18m), ct);
        var mouse = await mediator.Send(new CreateProductCommand("Wireless mouse", "8471", 799m, 18m), ct);
        var paper = await mediator.Send(new CreateProductCommand("A4 paper (500 sheets)", "4802", 320m, 12m), ct);
        var notebook = await mediator.Send(new CreateProductCommand("Notebook", "4820", 60m, 12m), ct);
        await mediator.Send(new CreateProductCommand("Packed rice 5kg", "1006", 410m, 5m), ct);

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var n = 0;
        foreach (var back in new[] { 330, 300, 270, 240, 210, 180, 150, 120, 90, 60, 45, 30, 20, 10, 5 })
        {
            foreach (var customer in customers)
            {
                n++;
                var issueDate = today.AddDays(-(back - n % 4));
                var firstProduct = n % 3 == 0 ? laptopStand : paper;
                var invoice = await mediator.Send(new CreateInvoiceCommand(
                    customer.Id, $"DEMO-{n:0000}", issueDate, issueDate.AddDays(15), "Demo invoice",
                    [
                        new CreateInvoiceLine(firstProduct.Id, null, null, n % 5 + 1, null, null),
                        new CreateInvoiceLine(mouse.Id, null, null, 2, null, null),
                        new CreateInvoiceLine(notebook.Id, null, null, 10, null, null),
                    ]), ct);

                if (back <= 5)
                    continue; // the newest stay drafts

                var issued = await mediator.Send(new IssueInvoiceCommand(invoice.Id), ct);

                // Older invoices are mostly paid, some part-paid, the rest stay open.
                decimal? payment = null;
                if (back >= 60 && n % 4 != 0)
                    payment = issued.GrandTotal;
                else if (back >= 30 && n % 4 == 0)
                    payment = Math.Round(issued.GrandTotal / 2m, 2, MidpointRounding.ToZero);

                if (payment is { } amount)
                    await mediator.Send(new RecordPaymentCommand(invoice.Id, $"seed-{invoice.Id:N}", amount, "INR", "demo"), ct);
            }
        }
    }
}
