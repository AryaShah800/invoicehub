using FluentAssertions;
using InvoiceHub.Application.Abstractions;
using InvoiceHub.Application.Common;
using InvoiceHub.Application.Payments;
using InvoiceHub.Domain.Common;
using InvoiceHub.Domain.Customers;
using InvoiceHub.Domain.Invoices;
using InvoiceHub.Infrastructure.Persistence;
using InvoiceHub.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Moq;

namespace InvoiceHub.UnitTests.Payments;

/// <summary>
/// The handler runs against the real DbContext (tenant query filters and write guard included) on the
/// EF Core in-memory provider. Its collaborators (notifier, tenant assigner, clock, logger) are Moq mocks.
/// </summary>
public sealed class RecordPaymentCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 9, 30, 0, TimeSpan.Zero);

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly InMemoryDatabaseRoot _root = new();
    private readonly string _databaseName = Guid.NewGuid().ToString();

    // The webhook has no signed-in user, so the request starts with no tenant until the handler assigns one.
    private Guid? _currentTenant;

    private readonly Mock<ITenantProvider> _tenantProvider = new();
    private readonly Mock<ITenantAssigner> _tenantAssigner = new();
    private readonly Mock<IPaymentNotifier> _notifier = new();
    private readonly Mock<TimeProvider> _clock = new();
    private readonly Mock<ILogger<RecordPaymentCommandHandler>> _logger = new();

    public RecordPaymentCommandHandlerTests()
    {
        _tenantProvider.SetupGet(p => p.TenantId).Returns(() => _currentTenant);
        _tenantAssigner.Setup(a => a.Assign(It.IsAny<Guid>())).Callback<Guid>(id => _currentTenant = id);
        _clock.Setup(c => c.GetUtcNow()).Returns(Now);
    }

    [Fact]
    public async Task A_new_payment_is_saved_and_pushed_to_the_owning_tenant()
    {
        var invoice = await SeedIssuedInvoiceAsync(_tenantA, "INV-A1");
        using var cts = new CancellationTokenSource();

        var result = await HandleAsync(new RecordPaymentCommand(invoice.Id, "evt_1", 500m, "INR", "UPI-1"), cts.Token);

        result.Should().BeEquivalentTo(new RecordPaymentResult(
            Duplicate: false, invoice.Id, InvoiceStatus.PartiallyPaid.ToString(), AmountPaid: 500m, BalanceDue: 680m));

        _notifier.Verify(n => n.PaymentReceivedAsync(
            _tenantA,
            new PaymentReceivedMessage(invoice.Id, "INV-A1", "evt_1", 500m, 500m, 680m, "PartiallyPaid", Now.UtcDateTime),
            cts.Token), Times.Once);

        var saved = await PaymentsOfAsync(_tenantA);
        saved.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { InvoiceId = invoice.Id, EventId = "evt_1", Amount = 500m, Reference = "UPI-1", ReceivedAtUtc = Now.UtcDateTime });
    }

    [Fact]
    public async Task The_tenant_that_owns_the_invoice_is_assigned_exactly_once()
    {
        await SeedIssuedInvoiceAsync(_tenantB, "INV-B1");
        var invoiceOfA = await SeedIssuedInvoiceAsync(_tenantA, "INV-A1");

        await HandleAsync(new RecordPaymentCommand(invoiceOfA.Id, "evt_1", 100m, "INR", null));

        _tenantAssigner.Verify(a => a.Assign(_tenantA), Times.Once);
        _tenantAssigner.Verify(a => a.Assign(It.Is<Guid>(id => id != _tenantA)), Times.Never);
        (await PaymentsOfAsync(_tenantB)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_retried_event_is_reported_as_duplicate_without_paying_or_notifying_again()
    {
        var invoice = await SeedIssuedInvoiceAsync(_tenantA, "INV-A1", alreadyPaid: ("evt_1", 500m));

        var result = await HandleAsync(new RecordPaymentCommand(invoice.Id, "evt_1", 500m, "INR", null));

        result.Duplicate.Should().BeTrue();
        result.AmountPaid.Should().Be(500m, "the retry must not apply the payment a second time");
        result.BalanceDue.Should().Be(680m);
        (await PaymentsOfAsync(_tenantA)).Should().ContainSingle();
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_event_id_already_used_for_another_invoice_is_a_conflict()
    {
        await SeedIssuedInvoiceAsync(_tenantA, "INV-A1", alreadyPaid: ("evt_1", 100m));
        var other = await SeedIssuedInvoiceAsync(_tenantA, "INV-A2");

        var act = () => HandleAsync(new RecordPaymentCommand(other.Id, "evt_1", 100m, "INR", null));

        await act.Should().ThrowAsync<ConflictException>().WithMessage("*evt_1*different invoice*");
        _notifier.VerifyNoOtherCalls();
        (await PaymentsOfAsync(_tenantA)).Should().ContainSingle(p => p.EventId == "evt_1");
    }

    [Fact]
    public async Task An_unknown_invoice_is_not_found_and_no_tenant_is_assigned()
    {
        await SeedIssuedInvoiceAsync(_tenantA, "INV-A1");
        var unknownId = Guid.NewGuid();

        var act = () => HandleAsync(new RecordPaymentCommand(unknownId, "evt_1", 100m, "INR", null));

        await act.Should().ThrowAsync<NotFoundException>().WithMessage($"*{unknownId}*");
        _tenantAssigner.Verify(a => a.Assign(It.IsAny<Guid>()), Times.Never);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_overpayment_is_rejected_by_the_domain_and_nothing_is_saved_or_pushed()
    {
        var invoice = await SeedIssuedInvoiceAsync(_tenantA, "INV-A1");

        var act = () => HandleAsync(new RecordPaymentCommand(invoice.Id, "evt_1", 1180.01m, "INR", null));

        await act.Should().ThrowAsync<DomainException>().WithMessage("*balance due*");
        (await PaymentsOfAsync(_tenantA)).Should().BeEmpty();
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_failed_push_does_not_fail_the_webhook_or_undo_the_saved_payment()
    {
        var invoice = await SeedIssuedInvoiceAsync(_tenantA, "INV-A1");
        _notifier
            .Setup(n => n.PaymentReceivedAsync(It.IsAny<Guid>(), It.IsAny<PaymentReceivedMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SignalR is down"));

        var result = await HandleAsync(new RecordPaymentCommand(invoice.Id, "evt_1", 1180m, "INR", null));

        result.Should().BeEquivalentTo(new { Duplicate = false, InvoiceStatus = "Paid", BalanceDue = 0m });
        (await PaymentsOfAsync(_tenantA)).Should().ContainSingle();
        _logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("evt_1")),
            It.Is<InvalidOperationException>(ex => ex.Message == "SignalR is down"),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    private Task<RecordPaymentResult> HandleAsync(RecordPaymentCommand command, CancellationToken cancellationToken = default)
    {
        _currentTenant = null;
        var db = NewContext();
        var handler = new RecordPaymentCommandHandler(
            db, _tenantAssigner.Object, _notifier.Object, _clock.Object, _logger.Object);
        return handler.Handle(command, cancellationToken);
    }

    /// <summary>Saves an issued invoice of 1000.00 + 18% GST = 1180.00, optionally with one payment applied.</summary>
    private async Task<Invoice> SeedIssuedInvoiceAsync(Guid tenantId, string number, (string EventId, decimal Amount)? alreadyPaid = null)
    {
        _currentTenant = tenantId;
        await using var db = NewContext();

        var customer = new Customer(tenantId, "Buyer", "buyer@example.test", "27", new Address("1 Road", null, "Pune", "411001"));
        var invoice = new Invoice(tenantId, "27", customer, number, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));
        invoice.AddCustomLine("Consulting", "9983", 1, 1000m, 18m);
        invoice.Issue();
        db.Customers.Add(customer);
        db.Invoices.Add(invoice);
        if (alreadyPaid is { } paid)
            db.Payments.Add(invoice.RecordPayment(paid.EventId, paid.Amount, null, Now.UtcDateTime));

        await db.SaveChangesAsync();
        _currentTenant = null;
        return invoice;
    }

    private async Task<List<Payment>> PaymentsOfAsync(Guid tenantId)
    {
        _currentTenant = tenantId;
        await using var db = NewContext();
        return await db.Payments.AsNoTracking().ToListAsync();
    }

    private InvoiceHubDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<InvoiceHubDbContext>()
            .UseInMemoryDatabase(_databaseName, _root)
            .AddInterceptors(
                new TenantWriteGuardInterceptor(_tenantProvider.Object),
                new AuditInterceptor(TimeProvider.System))
            .Options;
        return new InvoiceHubDbContext(options, _tenantProvider.Object);
    }
}
