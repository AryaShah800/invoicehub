using System.IdentityModel.Tokens.Jwt;
using System.Text;
using InvoiceHub.Application.Auth;
using InvoiceHub.Infrastructure.Identity;
using Microsoft.IdentityModel.Tokens;

namespace InvoiceHub.UnitTests.Identity;

public sealed class JwtTokenGeneratorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    private static readonly JwtOptions Options = new()
    {
        Issuer = "InvoiceHub",
        Audience = "InvoiceHub.Clients",
        SigningKey = "unit-test-signing-key-that-is-long-enough-123",
        ExpiryMinutes = 30,
    };

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static TokenValidationParameters ValidationFor(JwtOptions options, bool validateLifetime = false) => new()
    {
        ValidIssuer = options.Issuer,
        ValidAudience = options.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
        ClockSkew = TimeSpan.Zero,
        NameClaimType = "sub",
        RoleClaimType = AppClaims.Role,
        ValidateLifetime = validateLifetime,
    };

    [Fact]
    public void Token_carries_user_tenant_and_role_claims()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var generator = new JwtTokenGenerator(Options, new FixedClock(Now));

        var (token, _) = generator.Generate(userId, "owner@acme.test", tenantId, Roles.Admin);

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(token, ValidationFor(Options), out _);
        Assert.Equal(userId.ToString(), principal.FindFirst("sub")?.Value);
        Assert.Equal("owner@acme.test", principal.FindFirst("email")?.Value);
        Assert.Equal(tenantId.ToString(), principal.FindFirst(AppClaims.TenantId)?.Value);
        Assert.True(principal.IsInRole(Roles.Admin));
    }

    [Fact]
    public void Token_expires_after_the_configured_minutes()
    {
        var generator = new JwtTokenGenerator(Options, new FixedClock(Now));

        var (_, expiresAtUtc) = generator.Generate(Guid.NewGuid(), "a@b.test", Guid.NewGuid(), Roles.Staff);

        Assert.Equal(Now.UtcDateTime.AddMinutes(30), expiresAtUtc);
    }

    [Fact]
    public void Token_signed_with_a_different_key_is_rejected()
    {
        var other = new JwtOptions
        {
            Issuer = Options.Issuer,
            Audience = Options.Audience,
            SigningKey = "a-completely-different-signing-key-1234567890",
        };
        var (token, _) = new JwtTokenGenerator(other, new FixedClock(Now))
            .Generate(Guid.NewGuid(), "a@b.test", Guid.NewGuid(), Roles.Staff);

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };

        Assert.ThrowsAny<SecurityTokenException>(() => handler.ValidateToken(token, ValidationFor(Options), out _));
    }

    [Fact]
    public void Token_is_rejected_after_it_expires()
    {
        var (token, _) = new JwtTokenGenerator(Options, new FixedClock(TimeProvider.System.GetUtcNow().AddHours(-2)))
            .Generate(Guid.NewGuid(), "a@b.test", Guid.NewGuid(), Roles.Staff);

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };

        Assert.Throws<SecurityTokenExpiredException>(() => handler.ValidateToken(token, ValidationFor(Options, validateLifetime: true), out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-short")]
    public void Options_with_a_missing_or_short_signing_key_are_invalid(string key)
    {
        var options = new JwtOptions { Issuer = "i", Audience = "a", SigningKey = key };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Options_with_a_long_enough_key_are_valid()
    {
        Options.Validate();
    }
}
