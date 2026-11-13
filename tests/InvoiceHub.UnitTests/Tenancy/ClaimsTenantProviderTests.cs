using System.Security.Claims;
using FluentAssertions;
using InvoiceHub.Api.Tenancy;
using Microsoft.AspNetCore.Http;
using Moq;

namespace InvoiceHub.UnitTests.Tenancy;

/// <summary>
/// ClaimsTenantProvider is scoped and can be created before authentication has run, so it must read the
/// claim from the accessor on every access rather than once. The accessor is mocked so each test controls
/// what HttpContext (if any) the provider sees at each moment.
/// </summary>
public sealed class ClaimsTenantProviderTests
{
    private readonly Mock<IHttpContextAccessor> _accessor = new();
    private HttpContext? _context;

    public ClaimsTenantProviderTests() => _accessor.SetupGet(a => a.HttpContext).Returns(() => _context);

    private ClaimsTenantProvider NewProvider() => new(_accessor.Object);

    [Fact]
    public void The_tenant_is_read_from_the_current_user_each_time_not_cached_at_construction()
    {
        var provider = NewProvider();
        _context = ContextWith();
        provider.TenantId.Should().BeNull("authentication has not run yet");

        var tenantId = Guid.NewGuid();
        _context = ContextWith(new Claim(ClaimsTenantProvider.ClaimType, tenantId.ToString()));

        provider.TenantId.Should().Be(tenantId);
        _accessor.VerifyGet(a => a.HttpContext, Times.AtLeast(2));
    }

    [Fact]
    public void With_no_http_context_the_provider_fails_closed_but_can_be_assigned()
    {
        var provider = NewProvider();
        provider.TenantId.Should().BeNull();

        var tenantId = Guid.NewGuid();
        provider.Assign(tenantId);

        provider.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public void A_signed_in_users_claim_wins_over_a_tenant_assigned_earlier_in_the_request()
    {
        var provider = NewProvider();
        provider.Assign(Guid.NewGuid());

        var own = Guid.NewGuid();
        _context = ContextWith(new Claim(ClaimsTenantProvider.ClaimType, own.ToString()));

        provider.TenantId.Should().Be(own);
    }

    [Fact]
    public void Assign_is_refused_once_the_user_has_a_tenant_claim_even_if_it_matches()
    {
        var own = Guid.NewGuid();
        _context = ContextWith(new Claim(ClaimsTenantProvider.ClaimType, own.ToString()));
        var provider = NewProvider();

        provider.Invoking(p => p.Assign(own))
            .Should().Throw<InvalidOperationException>().WithMessage("*cannot be overridden*");
    }

    [Fact]
    public void An_unusable_tenant_claim_counts_as_no_user_so_a_webhook_tenant_can_still_be_assigned()
    {
        _context = ContextWith(new Claim(ClaimsTenantProvider.ClaimType, Guid.Empty.ToString()));
        var provider = NewProvider();
        var tenantId = Guid.NewGuid();

        provider.Assign(tenantId);

        provider.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public void Only_the_tenant_id_claim_type_is_used()
    {
        _context = ContextWith(
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("tenant", Guid.NewGuid().ToString()));

        NewProvider().TenantId.Should().BeNull();
    }

    private static HttpContext ContextWith(params Claim[] claims) => new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test")),
    };
}
