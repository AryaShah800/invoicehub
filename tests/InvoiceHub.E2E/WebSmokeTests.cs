using Microsoft.Playwright;

namespace InvoiceHub.E2E;

/// <summary>
/// Runs only when E2E_WEB_URL is set (for example after "docker compose up"), so a plain
/// "dotnet test" stays hermetic. Uses the Edge that is installed on the machine, no browser download.
/// Variables: E2E_WEB_URL, E2E_EMAIL, E2E_PASSWORD (the demo login from .env).
/// </summary>
public sealed class E2EFactAttribute : FactAttribute
{
    public E2EFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("E2E_WEB_URL")))
            Skip = "Set E2E_WEB_URL (and E2E_EMAIL, E2E_PASSWORD) to run the browser smoke test.";
    }
}

public class WebSmokeTests
{
    [E2EFact]
    public async Task Login_dashboard_invoice_list_and_detail_render_without_console_errors()
    {
        var baseUrl = Environment.GetEnvironmentVariable("E2E_WEB_URL")!.TrimEnd('/');
        var email = Environment.GetEnvironmentVariable("E2E_EMAIL") ?? "demo@invoicehub.example";
        var password = Environment.GetEnvironmentVariable("E2E_PASSWORD")
            ?? throw new InvalidOperationException("E2E_PASSWORD is not set.");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = "msedge", Headless = true });
        var page = await browser.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, m) => { if (m.Type == "error") errors.Add(m.Text); };
        page.PageError += (_, e) => errors.Add(e);

        await page.GotoAsync(baseUrl + "/login");
        await page.FillAsync("#email", email);
        await page.FillAsync("#password", password);
        await page.ClickAsync("button[type=submit]");

        await page.WaitForSelectorAsync("#kpi-overdue-count");
        Assert.Contains("Dashboard", await page.InnerTextAsync("h1"));

        await page.ClickAsync("text=Invoices");
        await page.WaitForSelectorAsync("#invoice-table");
        var rows = await page.Locator("#invoice-table tbody tr").CountAsync();
        Assert.True(rows > 0, "the invoice list should contain the seeded demo invoices");

        await page.Locator("#invoice-table tbody tr a").First.ClickAsync();
        await page.WaitForSelectorAsync("#grand-total");
        Assert.True(await page.Locator("text=Download PDF").IsVisibleAsync());

        Assert.Empty(errors);
    }

    /// <summary>Only meaningful against a demo-mode deployment (Demo__Enabled=true on the Web service).</summary>
    [E2EFact]
    public async Task Demo_mode_login_page_offers_a_one_click_demo_sign_in()
    {
        var baseUrl = Environment.GetEnvironmentVariable("E2E_WEB_URL")!.TrimEnd('/');
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = "msedge", Headless = true });
        var page = await browser.NewPageAsync();

        await page.GotoAsync(baseUrl + "/login");
        await page.WaitForSelectorAsync(".demo-box");
        await page.ClickAsync("button.demo-login");

        await page.WaitForSelectorAsync("#kpi-overdue-count");
        Assert.Contains("Dashboard", await page.InnerTextAsync("h1"));
    }
}
