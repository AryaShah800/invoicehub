using InvoiceHub.Api.Auth;
using InvoiceHub.Application.Invoices;
using InvoiceHub.Application.Invoices.Commands.CreateInvoice;
using InvoiceHub.Application.Invoices.Commands.IssueInvoice;
using InvoiceHub.Application.Invoices.Queries.GetInvoiceById;
using InvoiceHub.Application.Common;
using InvoiceHub.Application.Invoices.Queries.ListInvoices;
using InvoiceHub.Domain.Invoices;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceHub.Api.Controllers;

[ApiController]
[Route("api/invoices")]
[Authorize(Policy = Policies.TenantMember)]
public sealed class InvoicesController(ISender sender) : ControllerBase
{
    /// <summary>Creates a draft invoice for one of the caller's customers.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateInvoiceCommand command, CancellationToken ct)
    {
        var invoice = await sender.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = invoice.Id }, invoice);
    }

    /// <summary>Issues a draft invoice. It then cannot be edited and can receive payments.</summary>
    [HttpPost("{id:guid}/issue")]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Issue(Guid id, CancellationToken ct) =>
        Ok(await sender.Send(new IssueInvoiceCommand(id), ct));

    /// <summary>Lists the caller's invoices, newest first, filtered by status, customer, issue date range or overdue.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<InvoiceSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] InvoiceStatus? status, [FromQuery] Guid? customerId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] bool? overdue, [FromQuery] int page = 1, [FromQuery] int pageSize = PagedResult<InvoiceSummaryDto>.DefaultPageSize,
        CancellationToken ct = default) =>
        Ok(await sender.Send(new ListInvoicesQuery(status, customerId, from, to, overdue, page, pageSize), ct));

    /// <summary>Gets one of the caller's invoices. Another tenant's invoice is a 404.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct) =>
        Ok(await sender.Send(new GetInvoiceByIdQuery(id), ct));
}
