namespace InvoiceHub.Application.Abstractions;

/// <summary>
/// Lets code that runs without a signed-in user (the payment webhook) choose the tenant for the rest of the
/// request, after it has proved that the request is genuine and found which tenant owns the data.
/// It cannot override the tenant of a signed-in user, and it can only be set once per request.
/// </summary>
public interface ITenantAssigner
{
    void Assign(Guid tenantId);
}
