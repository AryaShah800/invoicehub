using System.Security.Claims;
using InvoiceHub.Api.Tenancy;
using Microsoft.AspNetCore.Http;

namespace InvoiceHub.IntegrationTests.Tenancy;

public sealed class ClaimsTenantProviderTests
{
    [Fact]
    public void Reads_the_tenant_from_the_tenant_id_claim()
    {
        var tenantId = Guid.NewGuid();

        var provider = ProviderFor(new Claim(ClaimsTenantProvider.ClaimType, tenantId.ToString()));

        Assert.Equal(tenantId, provider.TenantId);
    }

    [Fact]
    public void Returns_null_when_the_claim_is_missing()
    {
        Assert.Null(ProviderFor().TenantId);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Returns_null_when_the_claim_is_not_a_usable_tenant_id(string value)
    {
        Assert.Null(ProviderFor(new Claim(ClaimsTenantProvider.ClaimType, value)).TenantId);
    }

    [Fact]
    public void Returns_null_outside_an_http_request()
    {
        var provider = new ClaimsTenantProvider(new HttpContextAccessor());

        Assert.Null(provider.TenantId);
    }

    [Fact]
    public void A_request_with_no_user_can_have_its_tenant_assigned()
    {
        var tenantId = Guid.NewGuid();
        var provider = ProviderFor();

        provider.Assign(tenantId);

        Assert.Equal(tenantId, provider.TenantId);
    }

    [Fact]
    public void Assigning_the_same_tenant_twice_is_fine_but_a_different_one_is_refused()
    {
        var first = Guid.NewGuid();
        var provider = ProviderFor();
        provider.Assign(first);

        provider.Assign(first);

        Assert.Throws<InvalidOperationException>(() => provider.Assign(Guid.NewGuid()));
        Assert.Equal(first, provider.TenantId);
    }

    [Fact]
    public void The_tenant_of_a_signed_in_user_cannot_be_overridden()
    {
        var own = Guid.NewGuid();
        var provider = ProviderFor(new Claim(ClaimsTenantProvider.ClaimType, own.ToString()));

        Assert.Throws<InvalidOperationException>(() => provider.Assign(Guid.NewGuid()));
        Assert.Equal(own, provider.TenantId);
    }

    [Fact]
    public void An_empty_tenant_id_cannot_be_assigned()
    {
        Assert.Throws<ArgumentException>(() => ProviderFor().Assign(Guid.Empty));
    }

    private static ClaimsTenantProvider ProviderFor(params Claim[] claims)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test")),
        };
        return new ClaimsTenantProvider(new HttpContextAccessor { HttpContext = context });
    }
}
