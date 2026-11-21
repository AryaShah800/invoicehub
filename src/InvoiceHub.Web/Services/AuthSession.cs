using InvoiceHub.Application.Auth;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace InvoiceHub.Web.Services;

/// <summary>
/// The signed-in user for one browser circuit. The token is kept in protected session storage (encrypted by the
/// server, cleared when the tab closes) so a page refresh does not sign the user out.
/// </summary>
public sealed class AuthSession(ProtectedSessionStorage storage)
{
    private const string Key = "invoicehub.auth";
    private bool _loaded;

    public AuthResponse? User { get; private set; }
    public string? Token => User?.AccessToken;
    public bool IsSignedIn => User is not null;

    /// <summary>Raised after sign-in or sign-out.</summary>
    public event Action? Changed;

    /// <summary>Loads a saved sign-in once. Needs an interactive circuit, so call it after the first render.</summary>
    public async Task<bool> EnsureLoadedAsync()
    {
        if (_loaded) return IsSignedIn;
        _loaded = true;
        try
        {
            var saved = await storage.GetAsync<AuthResponse>(Key);
            if (saved.Success && saved.Value is { } user && user.ExpiresAtUtc > DateTime.UtcNow)
                User = user;
        }
        catch (Exception)
        {
            // Unreadable or tampered storage means "not signed in".
        }
        return IsSignedIn;
    }

    public async Task SignInAsync(AuthResponse user)
    {
        User = user;
        _loaded = true;
        await storage.SetAsync(Key, user);
        Changed?.Invoke();
    }

    public async Task SignOutAsync()
    {
        User = null;
        try { await storage.DeleteAsync(Key); } catch (Exception) { /* circuit may be gone */ }
        Changed?.Invoke();
    }
}
