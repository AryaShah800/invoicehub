namespace InvoiceHub.Application.Abstractions;

public sealed record InvoiceOverdueMessage(
    Guid InvoiceId, string InvoiceNumber, string CustomerName, DateOnly DueDate, decimal BalanceDue);

public sealed record RecurringInvoiceGeneratedMessage(
    Guid RecurringInvoiceId, string TemplateName, Guid InvoiceId, string InvoiceNumber, decimal GrandTotal);

/// <summary>
/// Tells one tenant about something the background job did. The API implements it with SignalR; a mail or SMS
/// implementation can be added next to it without touching the job.
/// </summary>
public interface IInvoiceEventNotifier
{
    Task InvoiceOverdueAsync(Guid tenantId, InvoiceOverdueMessage message, CancellationToken cancellationToken);

    Task RecurringInvoiceGeneratedAsync(Guid tenantId, RecurringInvoiceGeneratedMessage message, CancellationToken cancellationToken);
}
