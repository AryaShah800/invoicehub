namespace InvoiceHub.Application.Auth;

/// <summary>Names of the claims InvoiceHub puts in its access tokens.</summary>
public static class AppClaims
{
    public const string TenantId = "tenant_id";
    public const string Role = "role";
}
