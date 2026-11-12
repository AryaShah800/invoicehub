namespace InvoiceHub.Api.Auth;

public static class Policies
{
    /// <summary>Any signed-in user who belongs to a tenant.</summary>
    public const string TenantMember = nameof(TenantMember);

    /// <summary>Signed-in tenant Admins only.</summary>
    public const string AdminOnly = nameof(AdminOnly);
}
