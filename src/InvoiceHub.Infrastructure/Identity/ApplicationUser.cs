using Microsoft.AspNetCore.Identity;

namespace InvoiceHub.Infrastructure.Identity;

/// <summary>
/// A login. Every user belongs to exactly one tenant. Users are deliberately not covered by the
/// tenant query filter: they must be found by email before the tenant of the request is known.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }
}
