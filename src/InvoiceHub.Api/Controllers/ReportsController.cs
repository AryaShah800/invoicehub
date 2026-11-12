using InvoiceHub.Api.Auth;
using InvoiceHub.Application.Reports;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceHub.Api.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize(Policy = Policies.AdminOnly)]
public sealed class ReportsController(ISender sender) : ControllerBase
{
    /// <summary>GST collected by slab for invoices issued from..to (inclusive). Admin only.</summary>
    [HttpGet("gst-summary")]
    [ProducesResponseType(typeof(GstSummaryReport), StatusCodes.Status200OK)]
    public async Task<IActionResult> GstSummary([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        Ok(await sender.Send(new GetGstSummaryQuery(from, to), ct));
}
