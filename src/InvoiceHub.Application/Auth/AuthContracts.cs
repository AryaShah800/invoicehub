namespace InvoiceHub.Application.Auth;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Staff = "Staff";
}

public sealed record RegisterTenantRequest(
    string BusinessName,
    string Gstin,
    string StateCode,
    string Email,
    string Password);

public sealed record LoginRequest(string Email, string Password);

public sealed record CreateStaffUserRequest(string Email, string Password);

public sealed record AuthResponse(
    string AccessToken,
    DateTime ExpiresAtUtc,
    Guid UserId,
    Guid TenantId,
    string Email,
    string Role);

public sealed record CreatedUserResponse(Guid UserId, Guid TenantId, string Email, string Role);

public enum AuthFailure
{
    InvalidCredentials,
    LockedOut,
    TenantInactive,
    EmailTaken,
    GstinTaken,
    Validation,
    NoTenant,
}

/// <summary>Outcome of an auth operation: either a value or a failure with messages.</summary>
public sealed class AuthResult<T>
{
    private AuthResult() { }

    public T? Value { get; private init; }
    public AuthFailure? Failure { get; private init; }
    public IReadOnlyList<string> Errors { get; private init; } = [];
    public bool Succeeded => Failure is null;

    public static AuthResult<T> Success(T value) => new() { Value = value };

    public static AuthResult<T> Fail(AuthFailure failure, params string[] errors) =>
        new() { Failure = failure, Errors = errors };

    public static AuthResult<T> Fail(AuthFailure failure, IEnumerable<string> errors) =>
        new() { Failure = failure, Errors = errors.ToArray() };
}

public interface IAuthService
{
    /// <summary>Creates a new tenant together with its first user, who becomes the tenant Admin.</summary>
    Task<AuthResult<AuthResponse>> RegisterTenantAsync(RegisterTenantRequest request, CancellationToken ct);

    Task<AuthResult<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken ct);

    /// <summary>Creates a Staff user inside the current tenant.</summary>
    Task<AuthResult<CreatedUserResponse>> CreateStaffUserAsync(CreateStaffUserRequest request, CancellationToken ct);
}
