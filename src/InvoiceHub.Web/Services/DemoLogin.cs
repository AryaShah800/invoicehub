namespace InvoiceHub.Web.Services;

/// <summary>
/// Hosted-demo mode (Demo:Enabled). The credentials belong to a public demo account that is reset regularly,
/// so showing them on the login page is intended. Off by default.
/// </summary>
public sealed record DemoLogin(bool Enabled, string Email, string Password)
{
    public static DemoLogin Off { get; } = new(false, "", "");
}
