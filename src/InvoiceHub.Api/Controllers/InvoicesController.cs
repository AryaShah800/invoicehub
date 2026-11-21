using InvoiceHub.Api.Auth;
using InvoiceHub.Application.Invoices;
using InvoiceHub.Application.Invoices.Commands.CreateInvoice;
using InvoiceHub.Application.Invoices.Commands.IssueInvoice;
using InvoiceHub.Application.Invoices.Pdf;
using InvoiceHub.Application.Invoices.Queries.GetInvoiceById;
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

    /// <summary>Gets one of the caller's invoices. Another tenant's invoice is a 404.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct) =>
        Ok(await sender.Send(new GetInvoiceByIdQuery(id), ct));

    /// <summary>Downloads the invoice as a GST tax invoice PDF. Another tenant's invoice is a 404.</summary>
    [HttpGet("{id:guid}/pdf")]
    [Produces("application/pdf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPdf(Guid id, CancellationToken ct)
    {
        var pdf = await sender.Send(new GetInvoicePdfQuery(id), ct);
        return File(pdf.Content, "application/pdf", pdf.FileName);
    }
}
