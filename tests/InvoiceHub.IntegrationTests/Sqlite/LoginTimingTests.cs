using System.Net;
using System.Net.Http.Json;
using InvoiceHub.Application.Auth;
using InvoiceHub.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceHub.IntegrationTests.Sqlite;

/// <summary>
/// An unknown email must cost the same password-hash work as a wrong password, or the response time tells an
/// attacker which emails have accounts. Timing itself is too noisy to assert in CI, so count the hash checks.
/// </summary>
public sealed class LoginTimingTests(SqliteDemoApiFactory factory) : IClassFixture<SqliteDemoApiFactory>
{
    private sealed class CountingHasher : IPasswordHasher<ApplicationUser>
    {
        private readonly PasswordHasher<ApplicationUser> _inner = new();
        public int Verifications;

        public string HashPassword(ApplicationUser user, string password) => _inner.HashPassword(user, password);

        public PasswordVerificationResult VerifyHashedPassword(ApplicationUser user, string hashedPassword, string providedPassword)
        {
            Interlocked.Increment(ref Verifications);
            return _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
        }
    }

    [Fact]
    public async Task An_unknown_email_is_checked_against_a_password_hash_like_a_known_one()
    {
        var hasher = new CountingHasher();
        using var app = factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton<IPasswordHasher<ApplicationUser>>(hasher)));
        var client = app.CreateClient();

        var unknown = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("nobody-" + Guid.NewGuid() + "@example.test", "whatever-123"));
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(1, hasher.Verifications);

        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(factory.Demo.Email, "wrong-password"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(2, hasher.Verifications);
    }
}
