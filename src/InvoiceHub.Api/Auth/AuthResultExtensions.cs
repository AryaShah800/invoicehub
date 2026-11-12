using InvoiceHub.Application.Auth;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceHub.Api.Auth;

internal static class AuthResultExtensions
{
    /// <summary>Maps a failed auth result to a ProblemDetails response with a fitting status code.</summary>
    public static ObjectResult ToProblem<T>(this ControllerBase controller, AuthResult<T> result)
    {
        var (status, title) = result.Failure switch
        {
            AuthFailure.InvalidCredentials => (StatusCodes.Status401Unauthorized, "Authentication failed"),
            AuthFailure.LockedOut => (StatusCodes.Status423Locked, "Account locked"),
            AuthFailure.TenantInactive => (StatusCodes.Status403Forbidden, "Business account inactive"),
            AuthFailure.NoTenant => (StatusCodes.Status403Forbidden, "No tenant"),
            AuthFailure.EmailTaken or AuthFailure.GstinTaken => (StatusCodes.Status409Conflict, "Already registered"),
            _ => (StatusCodes.Status400BadRequest, "Request is not valid"),
        };

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = string.Join(" ", result.Errors),
        };
        return new ObjectResult(problem) { StatusCode = status };
    }
}
