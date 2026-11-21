using Bunit;
using InvoiceHub.Application.Common;
using InvoiceHub.Application.Customers;
using InvoiceHub.Application.Dashboard;
using InvoiceHub.Application.Invoices;
using InvoiceHub.Application.Invoices.Queries.ListInvoices;
using InvoiceHub.Application.Products;
using InvoiceHub.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace InvoiceHub.Web.Tests;

/// <summary>A bUnit context with the same services the app registers, and a fake API.</summary>
public abstract class UiTestBase : TestContext
{
    protected Mock<IInvoiceApi> Api { get; } = new();

    protected ToastService Toasts { get; } = new();

    protected UiTestBase()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var session = new AuthSession(new ProtectedSessionStorage(JSInterop.JSRuntime, new EphemeralDataProtectionProvider()));
        Services.AddSingleton(Api.Object);
        Services.AddSingleton(session);
        Services.AddSingleton(Toasts);
        Services.AddSingleton(new ApiEndpoint(new Uri("http://api.test")));
        Services.AddSingleton<InvoiceEvents>();
        Services.AddSingleton(DemoLogin.Off);
    }

    protected static InvoiceSummaryDto Summary(string number, string status = "Issued", bool overdue = false, decimal total = 1180m, string customer = "Acme Pvt Ltd") =>
        new(Guid.NewGuid(), number, status, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 16), Guid.NewGuid(), customer, total, 0m, total, overdue);

    protected static PagedResult<InvoiceSummaryDto> Page(params InvoiceSummaryDto[] items) =>
        new(items, 1, 15, items.Length);

    protected static InvoiceDto Invoice(Guid id, string status = "Draft", bool intraState = true) => new(
        id, "INV-100", status, new DateOnly(2026, 11, 1), new DateOnly(2099, 1, 1), Guid.NewGuid(), "Acme Pvt Ltd", "27",
        intraState ? "27" : "29", intraState, "Thanks",
        [new InvoiceLineDto(Guid.NewGuid(), 1, null, "Laptop stand", "8473", 2m, 1000m, 18m, 2000m, intraState ? 180m : 0m, intraState ? 180m : 0m, intraState ? 0m : 360m)],
        2000m, intraState ? 180m : 0m, intraState ? 180m : 0m, intraState ? 0m : 360m, 360m, 2360m, 0m, 2360m);

    protected static DashboardSummary Dashboard(int overdueCount = 3) => new(
        Enumerable.Range(1, 12).Select(m => new MonthlyRevenue(2026, m, m % 3, m * 10000m, m * 1800m, m * 11800m)).ToList(),
        50000m, 50000m, 80000m, overdueCount, 45000m, 120000m, 4);
}
