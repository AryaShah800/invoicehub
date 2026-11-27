using Bunit;
using Bunit.TestDoubles;
using InvoiceHub.Application.Auth;
using InvoiceHub.Application.Customers;
using InvoiceHub.Application.Payments;
using InvoiceHub.Application.Products;
using InvoiceHub.Web.Components.Pages;
using InvoiceHub.Web.Components.Shared;
using InvoiceHub.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;
using System.Net;

namespace InvoiceHub.Web.Tests;

public sealed class SharedComponentTests : UiTestBase
{
    [Fact]
    public void Revenue_chart_draws_one_bar_per_month_and_marks_the_latest()
    {
        var cut = RenderComponent<RevenueChart>(p => p.Add(c => c.Months, Dashboard().Months));

        Assert.Equal(12, cut.FindAll("rect.bar").Count);
        Assert.Single(cut.FindAll("rect.bar-now"));
        Assert.Contains("Billed value per month", cut.Find("svg").GetAttribute("aria-label"));
    }

    [Fact]
    public void Gst_split_shows_each_tax_with_its_share()
    {
        var cut = RenderComponent<GstSplit>(p => p.Add(c => c.Cgst, 25m).Add(c => c.Sgst, 25m).Add(c => c.Igst, 50m));

        Assert.Equal(3, cut.FindAll(".split-seg").Count);
        Assert.Contains("50%", cut.Markup);
        Assert.Contains("IGST", cut.Markup);
    }

    [Fact]
    public void Gst_split_skips_empty_segments_and_explains_an_empty_period()
    {
        var onlyIgst = RenderComponent<GstSplit>(p => p.Add(c => c.Igst, 10m));
        Assert.Single(onlyIgst.FindAll(".split-seg"));

        var none = RenderComponent<GstSplit>();
        Assert.Contains("No GST billed", none.Markup);
    }

    [Theory]
    [InlineData("Paid", false, "Paid", "badge-paid")]
    [InlineData("PartiallyPaid", false, "Part paid", "badge-part")]
    [InlineData("Issued", true, "Overdue", "badge-overdue")]
    [InlineData("Draft", false, "Draft", "badge-draft")]
    public void Status_badge_names_the_state_and_overdue_wins(string status, bool overdue, string label, string css)
    {
        var cut = RenderComponent<StatusBadge>(p => p.Add(c => c.Status, status).Add(c => c.IsOverdue, overdue));

        Assert.Equal(label, cut.Find("span").TextContent);
        Assert.Contains(css, cut.Find("span").ClassName);
    }

    [Fact]
    public void Toast_component_lists_toasts_and_dismisses_them()
    {
        var cut = RenderComponent<Toasts>();
        Toasts.Show("Invoice INV-1 paid", "Done");
        cut.WaitForAssertion(() => Assert.Contains("Invoice INV-1 paid", cut.Markup));

        cut.Find("button").Click();

        Assert.DoesNotContain("Invoice INV-1 paid", cut.Markup);
    }

    [Fact]
    public void A_toast_dismisses_itself_after_its_lifetime()
    {
        var cut = RenderComponent<Toasts>(p => p.Add(c => c.Lifetime, TimeSpan.FromMilliseconds(50)));
        Toasts.Show("Saved", "Draft saved");
        cut.WaitForAssertion(() => Assert.Contains("Saved", cut.Markup));

        cut.WaitForAssertion(() => Assert.Empty(Toasts.Toasts), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Leaving_the_page_cancels_pending_dismiss_timers()
    {
        var cut = RenderComponent<Toasts>(p => p.Add(c => c.Lifetime, TimeSpan.FromMilliseconds(100)));
        Toasts.Show("Saved", "Draft saved");

        DisposeComponents(); // the component is gone, e.g. the circuit closed
        await Task.Delay(300);

        // The timer was cancelled with the component: nothing ran against the disposed renderer.
        Assert.Single(Toasts.Toasts);
    }

    [Fact]
    public void A_paid_event_raises_a_success_toast_and_a_part_payment_an_info_toast()
    {
        var events = Services.GetRequiredService<InvoiceEvents>();

        events.Handle(new PaymentReceivedMessage(Guid.NewGuid(), "INV-1", "e1", 1180m, 1180m, 0m, "Paid", DateTime.UtcNow));
        events.Handle(new PaymentReceivedMessage(Guid.NewGuid(), "INV-2", "e2", 500m, 500m, 680m, "PartiallyPaid", DateTime.UtcNow));

        Assert.Collection(Toasts.Toasts,
            t => { Assert.Equal(ToastKind.Success, t.Kind); Assert.Contains("INV-1 paid", t.Title); Assert.Contains("₹1,180.00", t.Text); },
            t => { Assert.Equal(ToastKind.Info, t.Kind); Assert.Contains("₹680.00 still due", t.Text); });
    }

    [Fact]
    public void A_payment_event_is_forwarded_so_open_pages_can_refresh()
    {
        var events = Services.GetRequiredService<InvoiceEvents>();
        PaymentReceivedMessage? seen = null;
        events.PaymentReceived += m => seen = m;

        events.Handle(new PaymentReceivedMessage(Guid.NewGuid(), "INV-1", "e1", 1m, 1m, 0m, "Paid", DateTime.UtcNow));

        Assert.Equal("INV-1", seen?.InvoiceNumber);
    }
}

public sealed class DashboardPageTests : UiTestBase
{
    [Fact]
    public void Dashboard_shows_the_overdue_count_and_amounts()
    {
        Api.Setup(a => a.GetDashboardAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Dashboard(overdueCount: 3));

        var cut = RenderComponent<Dashboard>();

        cut.WaitForAssertion(() => Assert.Contains("3", cut.Find("#kpi-overdue-count").TextContent));
        Assert.Contains("₹45,000.00 past due", cut.Markup);
        Assert.Contains("₹1,20,000.00", cut.Markup);
        Assert.Equal(12, cut.FindAll("rect.bar").Count);
        Assert.NotNull(cut.Find(".kpi-overdue"));
    }

    [Fact]
    public void Dashboard_shows_no_alarm_when_nothing_is_overdue()
    {
        Api.Setup(a => a.GetDashboardAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Dashboard(overdueCount: 0));

        var cut = RenderComponent<Dashboard>();

        cut.WaitForAssertion(() => Assert.Contains("0", cut.Find("#kpi-overdue-count").TextContent));
        Assert.Empty(cut.FindAll(".kpi-overdue"));
    }

    [Fact]
    public void Dashboard_reports_an_unreachable_api()
    {
        Api.Setup(a => a.GetDashboardAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException());

        var cut = RenderComponent<Dashboard>();

        cut.WaitForAssertion(() => Assert.Contains("not reachable", cut.Find("[role=alert]").TextContent));
    }

    [Fact]
    public void Dashboard_reloads_when_a_payment_arrives()
    {
        Api.Setup(a => a.GetDashboardAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Dashboard());
        RenderComponent<Dashboard>().WaitForState(() => Api.Invocations.Count == 1);

        Services.GetRequiredService<InvoiceEvents>().Handle(
            new PaymentReceivedMessage(Guid.NewGuid(), "INV-1", "e", 1m, 1m, 0m, "Paid", DateTime.UtcNow));

        Api.Verify(a => a.GetDashboardAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}

public sealed class InvoiceListPageTests : UiTestBase
{
    private static readonly Guid CustomerId = Guid.NewGuid();

    public InvoiceListPageTests() =>
        Api.Setup(a => a.ListCustomersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CustomerDto(CustomerId, "Acme Pvt Ltd", "a@b.test", null, "27", "1 Road", null, "Pune", "411001")]);

    [Fact]
    public void Invoice_list_renders_rows_with_status_and_overdue_badges()
    {
        Api.Setup(a => a.ListInvoicesAsync(It.IsAny<InvoiceFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Page(Summary("INV-1", "Paid"), Summary("INV-2", "Issued", overdue: true)));

        var cut = RenderComponent<Invoices>();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("#invoice-table tbody tr").Count));
        Assert.Contains("badge-overdue", cut.Markup);
        Assert.Contains("href=\"/invoices/", cut.Markup);
    }

    [Fact]
    public void Applying_filters_sends_them_to_the_api_and_starts_at_page_one()
    {
        Api.Setup(a => a.ListInvoicesAsync(It.IsAny<InvoiceFilter>(), It.IsAny<CancellationToken>())).ReturnsAsync(Page(Summary("INV-1")));
        var cut = RenderComponent<Invoices>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#invoice-table")));

        cut.Find("#f-status").Change("Paid");
        cut.Find("#f-customer").Change(CustomerId.ToString());
        cut.Find("#f-from").Change("2026-01-01");
        cut.Find("#f-overdue").Change(true);
        cut.Find("form").Submit();

        Api.Verify(a => a.ListInvoicesAsync(
            It.Is<InvoiceFilter>(f => f.Status == "Paid" && f.CustomerId == CustomerId && f.From == new DateOnly(2026, 1, 1) && f.OverdueOnly && f.Page == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void An_empty_result_shows_an_empty_state()
    {
        Api.Setup(a => a.ListInvoicesAsync(It.IsAny<InvoiceFilter>(), It.IsAny<CancellationToken>())).ReturnsAsync(Page());

        var cut = RenderComponent<Invoices>();

        cut.WaitForAssertion(() => Assert.Contains("No invoices match", cut.Markup));
    }

    [Fact]
    public void The_next_button_asks_for_the_next_page()
    {
        Api.Setup(a => a.ListInvoicesAsync(It.IsAny<InvoiceFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvoiceHub.Application.Common.PagedResult<InvoiceHub.Application.Invoices.Queries.ListInvoices.InvoiceSummaryDto>([Summary("INV-1")], 1, 15, 40));
        var cut = RenderComponent<Invoices>();
        cut.WaitForAssertion(() => Assert.Contains("page 1 of 3", cut.Markup));

        cut.FindAll("button").First(b => b.TextContent == "Next").Click();

        Api.Verify(a => a.ListInvoicesAsync(It.Is<InvoiceFilter>(f => f.Page == 2), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void The_dashboard_link_can_preselect_a_status()
    {
        Api.Setup(a => a.ListInvoicesAsync(It.IsAny<InvoiceFilter>(), It.IsAny<CancellationToken>())).ReturnsAsync(Page());
        Services.GetRequiredService<FakeNavigationManager>().NavigateTo("/invoices?status=Draft");

        RenderComponent<Invoices>().WaitForAssertion(() =>
            Api.Verify(a => a.ListInvoicesAsync(It.Is<InvoiceFilter>(f => f.Status == "Draft"), It.IsAny<CancellationToken>()), Times.Once));
    }

    [Fact]
    public void Following_a_link_to_the_list_while_on_it_replaces_the_filter()
    {
        Api.Setup(a => a.ListInvoicesAsync(It.IsAny<InvoiceFilter>(), It.IsAny<CancellationToken>())).ReturnsAsync(Page());
        var nav = Services.GetRequiredService<FakeNavigationManager>();
        nav.NavigateTo("/invoices?status=Draft");
        var cut = RenderComponent<Invoices>();
        cut.WaitForAssertion(() =>
            Api.Verify(a => a.ListInvoicesAsync(It.Is<InvoiceFilter>(f => f.Status == "Draft"), It.IsAny<CancellationToken>()), Times.Once));

        nav.NavigateTo("/invoices?overdue=true");
        cut.WaitForAssertion(() =>
            Api.Verify(a => a.ListInvoicesAsync(It.Is<InvoiceFilter>(f => f.Status == null && f.OverdueOnly), It.IsAny<CancellationToken>()), Times.Once));
        Assert.Equal("", cut.Find("#f-status").GetAttribute("value") ?? "");

        // The nav menu's plain /invoices link clears the filter.
        nav.NavigateTo("/invoices");
        cut.WaitForAssertion(() =>
            Api.Verify(a => a.ListInvoicesAsync(It.Is<InvoiceFilter>(f => f.Status == null && !f.OverdueOnly), It.IsAny<CancellationToken>()), Times.Once));

        // Leaving the page does not reload the list.
        nav.NavigateTo("/customers");
        Api.Verify(a => a.ListInvoicesAsync(It.IsAny<InvoiceFilter>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }
}

public sealed class InvoiceDetailPageTests : UiTestBase
{
    private readonly Guid _id = Guid.NewGuid();

    [Theory]
    [InlineData(true, 2099)]
    [InlineData(false, 2000)]
    public void Overdue_badge_uses_the_API_flag(bool overdue, int dueYear)
    {
        Api.Setup(a => a.GetInvoiceAsync(_id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Invoice(_id, "Issued") with { IsOverdue = overdue, DueDate = new DateOnly(dueYear, 1, 1) });

        var cut = RenderComponent<InvoiceDetail>(p => p.Add(c => c.Id, _id));

        cut.WaitForAssertion(() => Assert.Equal(overdue ? "Overdue" : "Issued", cut.Find("h1 .badge").TextContent));
    }

    [Fact]
    public void A_draft_can_be_issued()
    {
        Api.Setup(a => a.GetInvoiceAsync(_id, It.IsAny<CancellationToken>())).ReturnsAsync(Invoice(_id, "Draft"));
        Api.Setup(a => a.IssueInvoiceAsync(_id, It.IsAny<CancellationToken>())).ReturnsAsync(Invoice(_id, "Issued"));
        var cut = RenderComponent<InvoiceDetail>(p => p.Add(c => c.Id, _id));
        cut.WaitForAssertion(() => cut.Find("#issue"));

        cut.Find("#issue").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#issue")));
        Assert.Contains("badge-issued", cut.Markup);
    }

    [Fact]
    public void An_issued_invoice_has_no_issue_button_and_shows_the_split_for_its_place_of_supply()
    {
        Api.Setup(a => a.GetInvoiceAsync(_id, It.IsAny<CancellationToken>())).ReturnsAsync(Invoice(_id, "Issued", intraState: false));

        var cut = RenderComponent<InvoiceDetail>(p => p.Add(c => c.Id, _id));

        cut.WaitForAssertion(() => Assert.Contains("₹2,360.00", cut.Find("#grand-total").TextContent));
        Assert.Empty(cut.FindAll("#issue"));
        Assert.Contains("IGST", cut.Markup);
        Assert.DoesNotContain("CGST", cut.Markup);
    }

    [Fact]
    public void Download_fetches_the_pdf_and_hands_it_to_the_browser()
    {
        var pdf = JSInterop.SetupVoid("invoicehub.download", _ => true);
        Api.Setup(a => a.GetInvoiceAsync(_id, It.IsAny<CancellationToken>())).ReturnsAsync(Invoice(_id, "Issued"));
        Api.Setup(a => a.GetInvoicePdfAsync(_id, It.IsAny<CancellationToken>())).ReturnsAsync("%PDF-1.7"u8.ToArray());
        var cut = RenderComponent<InvoiceDetail>(p => p.Add(c => c.Id, _id));
        cut.WaitForAssertion(() => cut.Find("#download-pdf"));

        cut.Find("#download-pdf").Click();

        pdf.VerifyInvoke("invoicehub.download");
        Assert.Equal("INV-100.pdf", pdf.Invocations.Single().Arguments[0]);
    }

    [Fact]
    public void Issuing_while_the_api_is_down_shows_a_message_and_keeps_the_button()
    {
        Api.Setup(a => a.GetInvoiceAsync(_id, It.IsAny<CancellationToken>())).ReturnsAsync(Invoice(_id, "Draft"));
        Api.Setup(a => a.IssueInvoiceAsync(_id, It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("down"));
        var cut = RenderComponent<InvoiceDetail>(p => p.Add(c => c.Id, _id));
        cut.WaitForAssertion(() => cut.Find("#issue"));

        cut.Find("#issue").Click();

        cut.WaitForAssertion(() => Assert.Contains("not reachable", cut.Markup));
        Assert.False(cut.Find("#issue").HasAttribute("disabled"));
    }

    [Fact]
    public void A_failed_browser_download_shows_a_message()
    {
        JSInterop.SetupVoid("invoicehub.download", _ => true).SetException(new JSException("blocked"));
        Api.Setup(a => a.GetInvoiceAsync(_id, It.IsAny<CancellationToken>())).ReturnsAsync(Invoice(_id, "Issued"));
        Api.Setup(a => a.GetInvoicePdfAsync(_id, It.IsAny<CancellationToken>())).ReturnsAsync("%PDF-1.7"u8.ToArray());
        var cut = RenderComponent<InvoiceDetail>(p => p.Add(c => c.Id, _id));
        cut.WaitForAssertion(() => cut.Find("#download-pdf"));

        cut.Find("#download-pdf").Click();

        cut.WaitForAssertion(() => Assert.Contains("could not save it", cut.Markup));
    }

    [Fact]
    public void A_missing_invoice_shows_the_api_message()
    {
        Api.Setup(a => a.GetInvoiceAsync(_id, It.IsAny<CancellationToken>())).ThrowsAsync(new ApiException(HttpStatusCode.NotFound, "Invoice was not found."));

        var cut = RenderComponent<InvoiceDetail>(p => p.Add(c => c.Id, _id));

        cut.WaitForAssertion(() => Assert.Contains("was not found", cut.Find("[role=alert]").TextContent));
    }

    [Fact]
    public void A_payment_for_this_invoice_reloads_it_but_another_invoice_does_not()
    {
        Api.Setup(a => a.GetInvoiceAsync(_id, It.IsAny<CancellationToken>())).ReturnsAsync(Invoice(_id, "Issued"));
        var cut = RenderComponent<InvoiceDetail>(p => p.Add(c => c.Id, _id));
        cut.WaitForAssertion(() => cut.Find("#grand-total"));
        var events = Services.GetRequiredService<InvoiceEvents>();

        events.Handle(new PaymentReceivedMessage(Guid.NewGuid(), "OTHER", "e1", 1m, 1m, 0m, "Paid", DateTime.UtcNow));
        Api.Verify(a => a.GetInvoiceAsync(_id, It.IsAny<CancellationToken>()), Times.Once);

        events.Handle(new PaymentReceivedMessage(_id, "INV-100", "e2", 1m, 1m, 0m, "Paid", DateTime.UtcNow));
        Api.Verify(a => a.GetInvoiceAsync(_id, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}

public sealed class NewInvoicePageTests : UiTestBase
{
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly Guid ProductId = Guid.NewGuid();

    public NewInvoicePageTests()
    {
        Api.Setup(a => a.ListCustomersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CustomerDto(CustomerId, "Acme Pvt Ltd", "a@b.test", null, "27", "1 Road", null, "Pune", "411001")]);
        Api.Setup(a => a.ListActiveProductsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ProductDto(ProductId, "Widget", "8471", 1000m, 18m, true)]);
    }

    [Fact]
    public void Default_dates_and_invoice_number_use_the_business_date()
    {
        var cut = RenderComponent<NewInvoice>();

        cut.WaitForAssertion(() => Assert.Equal("2026-11-27", cut.Find("#issue").GetAttribute("value")));
        Assert.Equal("2026-12-12", cut.Find("#due").GetAttribute("value"));
        Assert.StartsWith("INV-261127-", cut.Find("#number").GetAttribute("value"));
    }

    [Fact]
    public void Submitting_an_empty_form_shows_validation_messages_and_does_not_call_the_api()
    {
        var cut = RenderComponent<NewInvoice>();
        cut.WaitForAssertion(() => cut.Find("form"));

        cut.Find("form").Submit();

        Assert.Contains("Choose a customer.", cut.Markup);
        Assert.Contains("Line 1: choose a product.", cut.Markup);
        Api.Verify(a => a.CreateInvoiceAsync(It.IsAny<InvoiceHub.Application.Invoices.Commands.CreateInvoice.CreateInvoiceCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void A_valid_form_creates_the_draft_and_opens_it()
    {
        var created = Invoice(Guid.NewGuid());
        Api.Setup(a => a.CreateInvoiceAsync(It.IsAny<InvoiceHub.Application.Invoices.Commands.CreateInvoice.CreateInvoiceCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(created);
        var cut = RenderComponent<NewInvoice>();
        cut.WaitForAssertion(() => cut.Find("form"));

        cut.Find("#customer").Change(CustomerId.ToString());
        cut.Find("#number").Change("INV-77");
        cut.Find("#product-0").Change(ProductId.ToString());
        cut.Find("#qty-0").Change("3");
        cut.Find("form").Submit();

        Api.Verify(a => a.CreateInvoiceAsync(
            It.Is<InvoiceHub.Application.Invoices.Commands.CreateInvoice.CreateInvoiceCommand>(c =>
                c.CustomerId == CustomerId && c.InvoiceNumber == "INV-77" && c.Lines.Count == 1 && c.Lines[0].ProductId == ProductId && c.Lines[0].Quantity == 3m),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.EndsWith($"/invoices/{created.Id}", Services.GetRequiredService<FakeNavigationManager>().Uri);
    }

    [Fact]
    public void Lines_can_be_added_and_removed_and_the_estimate_follows()
    {
        var cut = RenderComponent<NewInvoice>();
        cut.WaitForAssertion(() => cut.Find("form"));

        cut.Find("#product-0").Change(ProductId.ToString());
        cut.Find("#qty-0").Change("2");
        Assert.Contains("₹2,360.00", cut.Find("#estimate").TextContent);

        cut.Find("#add-line").Click();
        Assert.Equal(2, cut.FindAll(".line-row").Count);

        cut.Find("button[aria-label='Remove line 2']").Click();
        Assert.Single(cut.FindAll(".line-row"));
    }

    [Fact]
    public void Server_side_rejections_are_shown_without_losing_the_form()
    {
        Api.Setup(a => a.CreateInvoiceAsync(It.IsAny<InvoiceHub.Application.Invoices.Commands.CreateInvoice.CreateInvoiceCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException(HttpStatusCode.Conflict, "Invoice number INV-77 already exists."));
        var cut = RenderComponent<NewInvoice>();
        cut.WaitForAssertion(() => cut.Find("form"));
        cut.Find("#customer").Change(CustomerId.ToString());
        cut.Find("#number").Change("INV-77");
        cut.Find("#product-0").Change(ProductId.ToString());

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("already exists", cut.Find("[role=alert]").TextContent));
        Assert.Equal("INV-77", cut.Find("#number").GetAttribute("value"));
    }
}

public sealed class LoginPageTests : UiTestBase
{
    [Fact]
    public void Empty_fields_are_flagged_before_calling_the_api()
    {
        var cut = RenderComponent<Login>();

        cut.Find("form").Submit();

        Assert.Contains("Enter your email.", cut.Markup);
        Assert.Contains("Enter your password.", cut.Markup);
        Api.Verify(a => a.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void A_wrong_password_shows_the_error_and_stays_on_the_page()
    {
        Api.Setup(a => a.LoginAsync("me@x.test", "bad", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException(HttpStatusCode.Unauthorized, "Email or password is not correct."));
        var cut = RenderComponent<Login>();

        cut.Find("#email").Change("me@x.test");
        cut.Find("#password").Change("bad");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("not correct", cut.Find("[role=alert]").TextContent));
        Assert.EndsWith("/", Services.GetRequiredService<FakeNavigationManager>().BaseUri);
    }

    [Fact]
    public void A_good_login_opens_the_dashboard()
    {
        Api.Setup(a => a.LoginAsync("me@x.test", "good", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthResponse("token", DateTime.UtcNow.AddHours(1), Guid.NewGuid(), Guid.NewGuid(), "me@x.test", "Admin"));
        var cut = RenderComponent<Login>();

        cut.Find("#email").Change("me@x.test");
        cut.Find("#password").Change("good");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.True(Services.GetRequiredService<AuthSession>().IsSignedIn));
        Assert.Equal("token", Services.GetRequiredService<AuthSession>().Token);
    }

    [Fact]
    public void The_demo_panel_is_hidden_unless_demo_mode_is_on()
    {
        var cut = RenderComponent<Login>();

        Assert.Empty(cut.FindAll(".demo-box"));
        Assert.DoesNotContain("Use demo account", cut.Markup);
    }

    [Fact]
    public void Demo_mode_shows_the_public_credentials_and_a_one_click_sign_in()
    {
        Services.AddSingleton(new DemoLogin(true, "demo@invoicehub.example", "Demo#Pass2026"));
        Api.Setup(a => a.LoginAsync("demo@invoicehub.example", "Demo#Pass2026", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthResponse("demo-token", DateTime.UtcNow.AddHours(1), Guid.NewGuid(), Guid.NewGuid(), "demo@invoicehub.example", "Admin"));
        var cut = RenderComponent<Login>();

        Assert.Contains("demo@invoicehub.example", cut.Find(".demo-box").TextContent);
        Assert.Contains("Demo#Pass2026", cut.Find(".demo-box").TextContent);

        cut.Find("button.demo-login").Click();

        cut.WaitForAssertion(() => Assert.Equal("demo-token", Services.GetRequiredService<AuthSession>().Token));
    }
}
