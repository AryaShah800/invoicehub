using InvoiceHub.Api.Auth;
using InvoiceHub.Application.Products;
using InvoiceHub.Application.Products.Commands;
using InvoiceHub.Application.Products.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceHub.Api.Controllers;

[ApiController]
[Route("api/products")]
[Authorize(Policy = Policies.TenantMember)]
public sealed class ProductsController(ISender sender) : ControllerBase
{
    /// <summary>Adds a product to the catalogue. Admin only, because price and GST rate flow into invoices.</summary>
    [HttpPost]
    [Authorize(Policy = Policies.AdminOnly)]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateProductCommand command, CancellationToken ct)
    {
        var product = await sender.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct) =>
        Ok(await sender.Send(new GetProductByIdQuery(id), ct));
}
