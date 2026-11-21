using InvoiceHub.Api.Auth;
using InvoiceHub.Application.Dashboard;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceHub.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize(Policy = Policies.TenantMember)]
public sealed class DashboardController(ISender sender) : ControllerBase
{
    /// <summary>Monthly billed value, GST split, and overdue/outstanding amounts for the caller's tenant.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(DashboardSummary), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromQuery] int months = 12, CancellationToken ct = default) =>
        Ok(await sender.Send(new GetDashboardQuery(months), ct));
}
