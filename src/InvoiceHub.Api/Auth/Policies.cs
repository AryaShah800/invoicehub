using InvoiceHub.Application.Auth;
using Microsoft.AspNetCore.Authorization;

namespace InvoiceHub.Api.Auth;

public static class Policies
{
    /// <summary>
    /// Requires a signed-in user whose token carries a usable tenant id (a non-empty GUID),
    /// matching what ClaimsTenantProvider accepts.
    /// </summary>
    public static AuthorizationPolicyBuilder RequireTenant(this AuthorizationPolicyBuilder builder) =>
        builder
            .RequireAuthenticatedUser()
            .RequireAssertion(context =>
                Guid.TryParse(context.User.FindFirst(AppClaims.TenantId)?.Value, out var id) && id != Guid.Empty);

    /// <summary>Any signed-in user who belongs to a tenant.</summary>
    public const string TenantMember = nameof(TenantMember);

    /// <summary>Signed-in tenant Admins only.</summary>
    public const string AdminOnly = nameof(AdminOnly);
}
