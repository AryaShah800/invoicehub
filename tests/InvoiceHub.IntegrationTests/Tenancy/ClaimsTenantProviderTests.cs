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

    private static ClaimsTenantProvider ProviderFor(params Claim[] claims)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test")),
        };
        return new ClaimsTenantProvider(new HttpContextAccessor { HttpContext = context });
    }
}
