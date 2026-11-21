using InvoiceHub.Api.Auth;
using InvoiceHub.Application.Customers;
using InvoiceHub.Application.Customers.Commands;
using InvoiceHub.Application.Customers.Queries;
using InvoiceHub.Application.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceHub.Api.Controllers;

[ApiController]
[Route("api/customers")]
[Authorize(Policy = Policies.TenantMember)]
public sealed class CustomersController(ISender sender) : ControllerBase
{
    /// <summary>Adds a customer (buyer) to the caller's tenant. The state code decides the place of supply.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(CustomerDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateCustomerCommand command, CancellationToken ct)
    {
        var customer = await sender.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = customer.Id }, customer);
    }

    /// <summary>Lists the caller's customers, paged, optionally filtered by name or email.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<CustomerDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = PagedResult<CustomerDto>.DefaultPageSize, CancellationToken ct = default) =>
        Ok(await sender.Send(new ListCustomersQuery(search, page, pageSize), ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CustomerDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct) =>
        Ok(await sender.Send(new GetCustomerByIdQuery(id), ct));
}
