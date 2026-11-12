using InvoiceHub.Application.Payments;

namespace InvoiceHub.Application.Abstractions;

/// <summary>Tells connected clients of one tenant that a payment arrived. Implemented with SignalR in the API.</summary>
public interface IPaymentNotifier
{
    Task PaymentReceivedAsync(Guid tenantId, PaymentReceivedMessage message, CancellationToken cancellationToken);
}
