using InvoiceHub.Api.Auth;
using InvoiceHub.Application.Abstractions;
using InvoiceHub.Application.Auth;
using InvoiceHub.Application.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace InvoiceHub.Api.Hubs;

/// <summary>What the server can call on a connected browser or app.</summary>
public interface IInvoiceClient
{
    Task PaymentReceived(PaymentReceivedMessage message);

    Task InvoiceOverdue(InvoiceOverdueMessage message);

    Task RecurringInvoiceGenerated(RecurringInvoiceGeneratedMessage message);
}

/// <summary>
/// Live updates for one business. Each connection joins the group of its own tenant, taken from the
/// signed-in user's tenant_id claim, so events for one tenant never reach another. The hub has no methods
/// clients can call: it only receives connections and the server pushes to the groups.
/// </summary>
[Authorize(Policy = Policies.TenantMember)]
public sealed class InvoiceEventsHub : Hub<IInvoiceClient>
{
    public const string Path = "/hubs/invoices";

    public static string GroupName(Guid tenantId) => $"tenant:{tenantId:D}";

    public override async Task OnConnectedAsync()
    {
        var claim = Context.User?.FindFirst(AppClaims.TenantId)?.Value;
        if (!Guid.TryParse(claim, out var tenantId) || tenantId == Guid.Empty)
        {
            // The authorization policy already requires a tenant, so this is only a second line of defence.
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(tenantId));
        await base.OnConnectedAsync();
    }
}
