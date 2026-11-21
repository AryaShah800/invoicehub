using InvoiceHub.Api.Auth;
using InvoiceHub.Application.Recurring;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceHub.Api.Controllers;

[ApiController]
[Route("api/recurring-invoices")]
[Authorize(Policy = Policies.TenantMember)]
public sealed class RecurringInvoicesController(ISender sender) : ControllerBase
{
    /// <summary>Creates a template that raises an invoice every N months. The background job generates them.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(RecurringInvoiceDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateRecurringInvoiceCommand command, CancellationToken ct)
    {
        var created = await sender.Send(command, ct);
        return CreatedAtAction(nameof(List), null, created);
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<RecurringInvoiceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await sender.Send(new ListRecurringInvoicesQuery(), ct));

    /// <summary>Stops a template from generating more invoices. Invoices already generated are untouched.</summary>
    [HttpPost("{id:guid}/pause")]
    [ProducesResponseType(typeof(RecurringInvoiceDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Pause(Guid id, CancellationToken ct) => Ok(await sender.Send(new PauseRecurringInvoiceCommand(id), ct));
}
