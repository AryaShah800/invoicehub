using InvoiceHub.Api.Auth;
using InvoiceHub.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceHub.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Policy = Policies.AdminOnly)]
public sealed class UsersController(IAuthService auth) : ControllerBase
{
    /// <summary>Creates a Staff user in the caller's own tenant. Admins only.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(CreatedUserResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateStaff(CreateStaffUserRequest request, CancellationToken ct)
    {
        var result = await auth.CreateStaffUserAsync(request, ct);
        return result.Succeeded ? StatusCode(StatusCodes.Status201Created, result.Value) : this.ToProblem(result);
    }
}
