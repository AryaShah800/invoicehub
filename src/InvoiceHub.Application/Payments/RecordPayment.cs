using FluentValidation;
using InvoiceHub.Application.Abstractions;
using InvoiceHub.Application.Common;
using InvoiceHub.Domain.Invoices;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InvoiceHub.Application.Payments;

/// <summary>What connected clients of a tenant receive when a payment is applied.</summary>
public sealed record PaymentReceivedMessage(
    Guid InvoiceId,
    string InvoiceNumber,
    string EventId,
    decimal Amount,
    decimal AmountPaid,
    decimal BalanceDue,
    string InvoiceStatus,
    DateTime ReceivedAtUtc);

/// <summary>A payment reported by the provider. The webhook controller has already verified the signature.</summary>
public sealed record RecordPaymentCommand(
    Guid InvoiceId,
    string EventId,
    decimal Amount,
    string Currency,
    string? Reference) : IRequest<RecordPaymentResult>;

/// <summary>Duplicate is true when this event had already been applied; nothing changed then.</summary>
public sealed record RecordPaymentResult(
    bool Duplicate,
    Guid InvoiceId,
    string InvoiceStatus,
    decimal AmountPaid,
    decimal BalanceDue);

public sealed class RecordPaymentCommandValidator : AbstractValidator<RecordPaymentCommand>
{
    public RecordPaymentCommandValidator()
    {
        RuleFor(c => c.InvoiceId).NotEmpty();
        RuleFor(c => c.EventId).NotEmpty().MaximumLength(Payment.EventIdMaxLength);
        RuleFor(c => c.Amount).GreaterThan(0).PrecisionScale(18, 2, ignoreTrailingZeros: true);
        RuleFor(c => c.Currency).Must(c => string.Equals(c, "INR", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Only INR payments are supported.");
        RuleFor(c => c.Reference).MaximumLength(Payment.ReferenceMaxLength);
    }
}

public sealed class RecordPaymentCommandHandler(
    IApplicationDbContext db,
    ITenantAssigner tenantAssigner,
    IPaymentNotifier notifier,
    TimeProvider timeProvider,
    ILogger<RecordPaymentCommandHandler> logger)
    : IRequestHandler<RecordPaymentCommand, RecordPaymentResult>
{
    public async Task<RecordPaymentResult> Handle(RecordPaymentCommand request, CancellationToken cancellationToken)
    {
        // The webhook has no signed-in user, so the tenant is not known yet. This is the one place that looks
        // an invoice up across tenants, and it reads nothing but the owning tenant id. Everything after
        // Assign() runs under the normal tenant filters and write guard.
        var tenantId = await db.Invoices
            .IgnoreQueryFilters()
            .Where(i => i.Id == request.InvoiceId)
            .Select(i => (Guid?)i.TenantId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(Invoice), request.InvoiceId);
        tenantAssigner.Assign(tenantId);

        var invoice = await db.Invoices
            .Include(i => i.Lines)
            .FirstAsync(i => i.Id == request.InvoiceId, cancellationToken);

        var alreadyApplied = await db.Payments
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.EventId == request.EventId, cancellationToken);
        if (alreadyApplied is not null)
        {
            if (alreadyApplied.InvoiceId != invoice.Id)
                throw new ConflictException($"Event '{request.EventId}' was already used for a different invoice.");

            // The provider is retrying an event we have applied. Answer success so it stops retrying.
            return ToResult(invoice, duplicate: true);
        }

        var receivedAt = timeProvider.GetUtcNow().UtcDateTime;
        var payment = invoice.RecordPayment(request.EventId, request.Amount, request.Reference, receivedAt);
        db.Payments.Add(payment);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two deliveries of the same event raced: both passed the check above, and the loser hit
            // the unique (TenantId, EventId) index or the invoice's row version. If the winner applied
            // this event, answer like any other retry instead of a 409, so the provider stops retrying.
            var winner = await db.Payments
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.EventId == request.EventId, cancellationToken);
            if (winner is null)
                throw;
            if (winner.InvoiceId != invoice.Id)
                throw new ConflictException($"Event '{request.EventId}' was already used for a different invoice.");

            var current = await db.Invoices
                .AsNoTracking()
                .Include(i => i.Lines)
                .FirstAsync(i => i.Id == request.InvoiceId, cancellationToken);
            return ToResult(current, duplicate: true);
        }

        await NotifyAsync(invoice, payment, cancellationToken);
        return ToResult(invoice, duplicate: false);
    }

    private async Task NotifyAsync(Invoice invoice, Payment payment, CancellationToken cancellationToken)
    {
        // The payment is already saved. A failed push must not make the provider retry and must not undo it.
        try
        {
            await notifier.PaymentReceivedAsync(
                invoice.TenantId,
                new PaymentReceivedMessage(
                    invoice.Id, invoice.InvoiceNumber, payment.EventId, payment.Amount,
                    invoice.AmountPaid, invoice.BalanceDue, invoice.Status.ToString(), payment.ReceivedAtUtc),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Payment {EventId} was saved but the real-time notification failed.", payment.EventId);
        }
    }

    private static RecordPaymentResult ToResult(Invoice invoice, bool duplicate) =>
        new(duplicate, invoice.Id, invoice.Status.ToString(), invoice.AmountPaid, invoice.BalanceDue);
}
