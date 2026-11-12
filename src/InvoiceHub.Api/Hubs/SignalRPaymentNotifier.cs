using InvoiceHub.Application.Abstractions;
using InvoiceHub.Application.Payments;
using Microsoft.AspNetCore.SignalR;

namespace InvoiceHub.Api.Hubs;

/// <summary>Pushes payment events to the connected clients of the tenant that owns the invoice, and nobody else.</summary>
public sealed class SignalRPaymentNotifier(IHubContext<InvoiceEventsHub, IInvoiceClient> hub) : IPaymentNotifier
{
    public Task PaymentReceivedAsync(Guid tenantId, PaymentReceivedMessage message, CancellationToken cancellationToken) =>
        hub.Clients.Group(InvoiceEventsHub.GroupName(tenantId)).PaymentReceived(message);
}
