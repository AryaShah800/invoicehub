using InvoiceHub.Application.Abstractions;
using InvoiceHub.Application.Auth;

namespace InvoiceHub.Api.Tenancy;

/// <summary>
/// Reads the tenant from the "tenant_id" claim of the authenticated user.
/// The claim is issued in the JWT once login is added; until then no request carries it.
/// </summary>
public sealed class ClaimsTenantProvider(IHttpContextAccessor httpContextAccessor) : ITenantProvider
{
    public const string ClaimType = AppClaims.TenantId;

    public Guid? TenantId
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.User.FindFirst(ClaimType)?.Value;
            return Guid.TryParse(value, out var id) && id != Guid.Empty ? id : null;
        }
    }
}
