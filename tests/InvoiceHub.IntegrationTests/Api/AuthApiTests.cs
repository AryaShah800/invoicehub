using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using InvoiceHub.Application.Auth;
using InvoiceHub.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceHub.IntegrationTests.Api;

[Collection(ApiCollection.Name)]
public sealed class AuthApiTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    // ---- registration ----

    [Fact]
    public async Task Registering_a_business_returns_a_token_for_a_new_tenant_admin()
    {
        var request = TestData.NewRegistration();

        var response = await _client.PostAsync("/api/auth/register", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        Assert.Equal(Roles.Admin, auth.Role);
        Assert.Equal(request.Email, auth.Email);
        Assert.NotEqual(Guid.Empty, auth.TenantId);
        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
    }

    [Fact]
    public async Task The_token_from_registration_identifies_the_user_on_me()
    {
        var auth = await _client.RegisterAsync();

        var response = await _client.GetAsync("/api/auth/me", auth.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(auth.TenantId.ToString(), json.RootElement.GetProperty("tenantId").GetString());
        Assert.Equal(auth.UserId.ToString(), json.RootElement.GetProperty("userId").GetString());
        Assert.Equal(Roles.Admin, json.RootElement.GetProperty("roles")[0].GetString());
    }

    [Fact]
    public async Task Registering_the_same_gstin_twice_is_a_conflict()
    {
        var gstin = TestData.NewGstin();
        await _client.RegisterAsync(TestData.NewRegistration(gstin: gstin));

        var response = await _client.PostAsync("/api/auth/register", TestData.NewRegistration(gstin: gstin));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Registering_the_same_email_twice_is_a_conflict()
    {
        var email = TestData.NewEmail();
        await _client.RegisterAsync(TestData.NewRegistration(email: email));

        var response = await _client.PostAsync("/api/auth/register", TestData.NewRegistration(email: email));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_failed_registration_leaves_no_tenant_behind()
    {
        var gstin = TestData.NewGstin();

        var weak = await _client.PostAsync("/api/auth/register", TestData.NewRegistration(gstin: gstin, password: "short"));
        var retry = await _client.PostAsync("/api/auth/register", TestData.NewRegistration(gstin: gstin));

        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
    }

    [Theory]
    [InlineData("not-a-gstin", "27")]
    [InlineData("27AAPFU0939F1ZV", "99x")]
    public async Task Registering_with_an_invalid_gstin_or_state_is_rejected(string gstin, string stateCode)
    {
        var request = TestData.NewRegistration() with { Gstin = gstin, StateCode = stateCode };

        var response = await _client.PostAsync("/api/auth/register", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    public async Task Registering_with_an_invalid_email_is_rejected(string email)
    {
        var response = await _client.PostAsync("/api/auth/register", TestData.NewRegistration(email: email));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- login ----

    [Fact]
    public async Task Logging_in_with_the_right_password_returns_a_token()
    {
        var registered = await _client.RegisterAsync();

        var auth = await _client.LoginAsync(registered.Email, TestData.Password);

        Assert.Equal(registered.TenantId, auth.TenantId);
        Assert.Equal(Roles.Admin, auth.Role);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_give_the_same_401()
    {
        var registered = await _client.RegisterAsync();

        var wrongPassword = await _client.PostAsync("/api/auth/login", new LoginRequest(registered.Email, "Wrong!Passw0rd"));
        var unknownEmail = await _client.PostAsync("/api/auth/login", new LoginRequest(TestData.NewEmail(), TestData.Password));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal(await wrongPassword.Content.ReadAsStringAsync(), await unknownEmail.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_for_the_right_password()
    {
        var registered = await _client.RegisterAsync();
        for (var i = 0; i < 5; i++)
            await _client.PostAsync("/api/auth/login", new LoginRequest(registered.Email, "Wrong!Passw0rd"));

        var response = await _client.PostAsync("/api/auth/login", new LoginRequest(registered.Email, TestData.Password));

        Assert.Equal(HttpStatusCode.Locked, response.StatusCode);
    }

    // ---- token validation ----

    [Fact]
    public async Task Requests_without_a_token_are_rejected()
    {
        var response = await _client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_garbage_token_is_rejected()
    {
        var response = await _client.GetAsync("/api/auth/me", "not.a.token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_signed_with_another_key_is_rejected()
    {
        var forged = TokenWith(signingKey: "a-different-signing-key-0123456789-abcdef", issuedAt: DateTimeOffset.UtcNow);

        var response = await _client.GetAsync("/api/auth/me", forged);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_expired_token_is_rejected()
    {
        var expired = TokenWith(ApiFactory.SigningKey, issuedAt: DateTimeOffset.UtcNow.AddHours(-3));

        var response = await _client.GetAsync("/api/auth/me", expired);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_correctly_signed_token_without_a_usable_tenant_is_forbidden()
    {
        var emptyTenant = TokenWith(ApiFactory.SigningKey, DateTimeOffset.UtcNow, tenantId: Guid.Empty);
        var noTenantClaim = TokenWithoutTenant(ApiFactory.SigningKey);

        var emptyResponse = await _client.GetAsync("/api/auth/me", emptyTenant);
        var missingResponse = await _client.GetAsync("/api/auth/me", noTenantClaim);

        // Authenticated (the signature is valid) but not authorized: 403, not 401.
        Assert.Equal(HttpStatusCode.Forbidden, emptyResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, missingResponse.StatusCode);
    }

    [Fact]
    public async Task Everything_is_protected_unless_an_endpoint_opts_out()
    {
        var provider = factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        var fallback = await provider.GetFallbackPolicyAsync();

        Assert.NotNull(fallback);
        Assert.Contains(fallback.Requirements, r => r is DenyAnonymousAuthorizationRequirement);
    }

    // ---- roles and tenants ----

    [Fact]
    public async Task An_admin_can_create_a_staff_user_in_their_own_tenant()
    {
        var admin = await _client.RegisterAsync();
        var staffEmail = TestData.NewEmail();

        var created = await _client.PostAsync("/api/users", new CreateStaffUserRequest(staffEmail, TestData.Password), admin.AccessToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var staff = await _client.LoginAsync(staffEmail, TestData.Password);
        Assert.Equal(Roles.Staff, staff.Role);
        Assert.Equal(admin.TenantId, staff.TenantId);
    }

    [Fact]
    public async Task A_staff_user_cannot_create_users()
    {
        var admin = await _client.RegisterAsync();
        var staffEmail = TestData.NewEmail();
        await _client.PostAsync("/api/users", new CreateStaffUserRequest(staffEmail, TestData.Password), admin.AccessToken);
        var staff = await _client.LoginAsync(staffEmail, TestData.Password);

        var response = await _client.PostAsync("/api/users",
            new CreateStaffUserRequest(TestData.NewEmail(), TestData.Password), staff.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Creating_a_user_without_a_token_is_unauthorized()
    {
        var response = await _client.PostAsync("/api/users", new CreateStaffUserRequest(TestData.NewEmail(), TestData.Password));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Staff_are_created_in_the_callers_tenant_never_another_one()
    {
        var acme = await _client.RegisterAsync();
        var globex = await _client.RegisterAsync();
        var email = TestData.NewEmail();

        await _client.PostAsync("/api/users", new CreateStaffUserRequest(email, TestData.Password), globex.AccessToken);

        var staff = await _client.LoginAsync(email, TestData.Password);
        Assert.Equal(globex.TenantId, staff.TenantId);
        Assert.NotEqual(acme.TenantId, staff.TenantId);
    }

    [Fact]
    public async Task An_email_cannot_be_reused_by_another_tenant()
    {
        var acme = await _client.RegisterAsync();
        var globex = await _client.RegisterAsync();
        var email = TestData.NewEmail();
        await _client.PostAsync("/api/users", new CreateStaffUserRequest(email, TestData.Password), acme.AccessToken);

        var response = await _client.PostAsync("/api/users", new CreateStaffUserRequest(email, TestData.Password), globex.AccessToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---- helpers ----

    private static string TokenWith(string signingKey, DateTimeOffset issuedAt, Guid? tenantId = null)
    {
        var options = new JwtOptions
        {
            Issuer = "InvoiceHub",
            Audience = "InvoiceHub.Clients",
            SigningKey = signingKey,
            ExpiryMinutes = 60,
        };
        return new JwtTokenGenerator(options, new FixedClock(issuedAt))
            .Generate(Guid.NewGuid(), TestData.NewEmail(), tenantId ?? Guid.NewGuid(), Roles.Admin).Token;
    }

    private static string TokenWithoutTenant(string signingKey)
    {
        var claims = new[] { new System.Security.Claims.Claim("sub", Guid.NewGuid().ToString()) };
        var credentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(signingKey)),
            Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            "InvoiceHub", "InvoiceHub.Clients", claims, expires: DateTime.UtcNow.AddMinutes(10), signingCredentials: credentials);
        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
