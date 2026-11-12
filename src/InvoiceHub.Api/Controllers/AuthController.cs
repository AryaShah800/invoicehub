using InvoiceHub.Api.Auth;
using InvoiceHub.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceHub.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService auth) : ControllerBase
{
    /// <summary>Registers a new business (tenant) and its first user, who becomes the tenant Admin.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Register(RegisterTenantRequest request, CancellationToken ct)
    {
        var result = await auth.RegisterTenantAsync(request, ct);
        return result.Succeeded ? StatusCode(StatusCodes.Status201Created, result.Value) : this.ToProblem(result);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await auth.LoginAsync(request, ct);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result);
    }

    /// <summary>Shows who the access token belongs to.</summary>
    [HttpGet("me")]
    [Authorize(Policy = Policies.TenantMember)]
    public IActionResult Me() => Ok(new
    {
        UserId = User.FindFirst("sub")?.Value,
        Email = User.FindFirst("email")?.Value,
        TenantId = User.FindFirst(AppClaims.TenantId)?.Value,
        Roles = User.FindAll(AppClaims.Role).Select(c => c.Value),
    });
}
