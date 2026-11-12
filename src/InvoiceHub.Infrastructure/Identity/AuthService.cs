using System.Net.Mail;
using InvoiceHub.Application.Abstractions;
using InvoiceHub.Application.Auth;
using InvoiceHub.Domain.Common;
using InvoiceHub.Domain.Tenants;
using InvoiceHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Infrastructure.Identity;

public sealed class AuthService(
    InvoiceHubDbContext db,
    UserManager<ApplicationUser> users,
    IJwtTokenGenerator tokens,
    ITenantProvider tenantProvider) : IAuthService
{
    private const string InvalidCredentialsMessage = "Invalid email or password.";

    public async Task<AuthResult<AuthResponse>> RegisterTenantAsync(RegisterTenantRequest request, CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);
        if (email is null)
            return AuthResult<AuthResponse>.Fail(AuthFailure.Validation, "Email is not valid.");

        Tenant tenant;
        try
        {
            tenant = new Tenant(request.BusinessName, request.Gstin, request.StateCode);
        }
        catch (DomainException ex)
        {
            return AuthResult<AuthResponse>.Fail(AuthFailure.Validation, ex.Message);
        }

        if (await users.FindByEmailAsync(email) is not null)
            return AuthResult<AuthResponse>.Fail(AuthFailure.EmailTaken, "An account with this email already exists.");
        if (await db.Tenants.AnyAsync(t => t.Gstin == tenant.Gstin, ct))
            return AuthResult<AuthResponse>.Fail(AuthFailure.GstinTaken, "This GSTIN is already registered.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(ct);

            var user = new ApplicationUser { UserName = email, Email = email, TenantId = tenant.Id };
            var created = await users.CreateAsync(user, request.Password);
            if (!created.Succeeded)
            {
                await transaction.RollbackAsync(ct);
                return AuthResult<AuthResponse>.Fail(AuthFailure.Validation, created.Errors.Select(e => e.Description));
            }

            var roleAdded = await users.AddToRoleAsync(user, Roles.Admin);
            if (!roleAdded.Succeeded)
            {
                await transaction.RollbackAsync(ct);
                return AuthResult<AuthResponse>.Fail(AuthFailure.Validation, roleAdded.Errors.Select(e => e.Description));
            }

            await transaction.CommitAsync(ct);
            return AuthResult<AuthResponse>.Success(IssueToken(user, Roles.Admin));
        }
        catch (DbUpdateException)
        {
            // A concurrent registration won the race for the same email or GSTIN.
            await transaction.RollbackAsync(ct);
            return AuthResult<AuthResponse>.Fail(AuthFailure.EmailTaken, "This email or GSTIN is already registered.");
        }
    }

    public async Task<AuthResult<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);
        var user = email is null ? null : await users.FindByEmailAsync(email);
        if (user is null || string.IsNullOrEmpty(request.Password))
            return AuthResult<AuthResponse>.Fail(AuthFailure.InvalidCredentials, InvalidCredentialsMessage);

        if (await users.IsLockedOutAsync(user))
            return AuthResult<AuthResponse>.Fail(AuthFailure.LockedOut, "Too many failed attempts. Try again later.");

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            return AuthResult<AuthResponse>.Fail(AuthFailure.InvalidCredentials, InvalidCredentialsMessage);
        }

        await users.ResetAccessFailedCountAsync(user);

        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == user.TenantId, ct);
        if (tenant is null || !tenant.IsActive)
            return AuthResult<AuthResponse>.Fail(AuthFailure.TenantInactive, "This business account is inactive.");

        var role = await PrimaryRoleAsync(user);
        return AuthResult<AuthResponse>.Success(IssueToken(user, role));
    }

    public async Task<AuthResult<CreatedUserResponse>> CreateStaffUserAsync(CreateStaffUserRequest request, CancellationToken ct)
    {
        if (tenantProvider.TenantId is not { } tenantId)
            return AuthResult<CreatedUserResponse>.Fail(AuthFailure.NoTenant, "No tenant for this request.");

        var email = NormalizeEmail(request.Email);
        if (email is null)
            return AuthResult<CreatedUserResponse>.Fail(AuthFailure.Validation, "Email is not valid.");

        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null || !tenant.IsActive)
            return AuthResult<CreatedUserResponse>.Fail(AuthFailure.TenantInactive, "This business account is inactive.");

        if (await users.FindByEmailAsync(email) is not null)
            return AuthResult<CreatedUserResponse>.Fail(AuthFailure.EmailTaken, "An account with this email already exists.");

        var user = new ApplicationUser { UserName = email, Email = email, TenantId = tenantId };
        var created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded)
            return AuthResult<CreatedUserResponse>.Fail(AuthFailure.Validation, created.Errors.Select(e => e.Description));

        var roleAdded = await users.AddToRoleAsync(user, Roles.Staff);
        if (!roleAdded.Succeeded)
        {
            await users.DeleteAsync(user);
            return AuthResult<CreatedUserResponse>.Fail(AuthFailure.Validation, roleAdded.Errors.Select(e => e.Description));
        }

        return AuthResult<CreatedUserResponse>.Success(new CreatedUserResponse(user.Id, tenantId, email, Roles.Staff));
    }

    private AuthResponse IssueToken(ApplicationUser user, string role)
    {
        var (token, expiresAtUtc) = tokens.Generate(user.Id, user.Email!, user.TenantId, role);
        return new AuthResponse(token, expiresAtUtc, user.Id, user.TenantId, user.Email!, role);
    }

    private async Task<string> PrimaryRoleAsync(ApplicationUser user)
    {
        var roles = await users.GetRolesAsync(user);
        return roles.Contains(Roles.Admin) ? Roles.Admin : Roles.Staff;
    }

    private static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 256)
            return null;

        return MailAddress.TryCreate(email.Trim(), out var address) && address.Address == email.Trim()
            ? address.Address.ToLowerInvariant()
            : null;
    }
}
