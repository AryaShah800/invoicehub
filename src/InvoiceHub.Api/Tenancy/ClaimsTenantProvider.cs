using InvoiceHub.Application.Abstractions;
using InvoiceHub.Application.Auth;

namespace InvoiceHub.Api.Tenancy;

/// <summary>
/// The tenant of the current request. Normally it comes from the "tenant_id" claim of the signed-in user.
/// Requests with no user (the payment webhook) can have the tenant assigned once, after it has been
/// found from verified data. A signed-in user's tenant can never be overridden.
/// </summary>
public sealed class ClaimsTenantProvider(IHttpContextAccessor httpContextAccessor) : ITenantProvider, ITenantAssigner
{
    public const string ClaimType = AppClaims.TenantId;

    private Guid? _assigned;

    public Guid? TenantId => ClaimTenantId ?? _assigned;

    private Guid? ClaimTenantId
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.User.FindFirst(ClaimType)?.Value;
            return Guid.TryParse(value, out var id) && id != Guid.Empty ? id : null;
        }
    }

    public void Assign(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (ClaimTenantId is not null)
            throw new InvalidOperationException("The tenant of a signed-in user cannot be overridden.");
        if (_assigned is not null && _assigned != tenantId)
            throw new InvalidOperationException("The tenant of this request has already been set.");

        _assigned = tenantId;
    }
}
