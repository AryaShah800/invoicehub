using InvoiceHub.Application.Payments;
using Microsoft.AspNetCore.SignalR.Client;

namespace InvoiceHub.Web.Services;

/// <summary>
/// Listens to the API's SignalR hub for the signed-in tenant and shows a toast when an invoice is paid.
/// The connection runs from this server to the API (not from the browser), authenticated with the user's token.
/// </summary>
public sealed class InvoiceEvents(ApiEndpoint endpoint, AuthSession session, ToastService toasts) : IAsyncDisposable
{
    private HubConnection? _connection;

    /// <summary>Raised for every payment so open pages can refresh themselves.</summary>
    public event Action<PaymentReceivedMessage>? PaymentReceived;

    public async Task StartAsync()
    {
        if (_connection is not null || !session.IsSignedIn)
            return;

        _connection = new HubConnectionBuilder()
            .WithUrl(new Uri(endpoint.BaseUri, "/hubs/invoices"), o => o.AccessTokenProvider = () => Task.FromResult(session.Token))
            .WithAutomaticReconnect()
            .Build();
        _connection.On<PaymentReceivedMessage>("PaymentReceived", Handle);

        try { await _connection.StartAsync(); }
        catch (Exception)
        {
            // Live toasts are a convenience. The UI works without them, so a failed connection is not an error page.
        }
    }

    internal void Handle(PaymentReceivedMessage message)
    {
        var paid = message.InvoiceStatus == "Paid";
        toasts.Show(
            paid ? $"Invoice {message.InvoiceNumber} paid" : $"Payment received for {message.InvoiceNumber}",
            paid ? $"{Money.Format(message.Amount)} received. Nothing left to collect." : $"{Money.Format(message.Amount)} received, {Money.Format(message.BalanceDue)} still due.",
            paid ? ToastKind.Success : ToastKind.Info);
        PaymentReceived?.Invoke(message);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
    }
}
