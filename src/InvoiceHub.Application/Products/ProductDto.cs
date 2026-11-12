using InvoiceHub.Domain.Products;

namespace InvoiceHub.Application.Products;

public sealed record ProductDto(Guid Id, string Name, string HsnCode, decimal UnitPrice, decimal GstRate, bool IsActive);

internal static class ProductMapping
{
    public static ProductDto ToDto(this Product p) => new(p.Id, p.Name, p.HsnCode, p.UnitPrice, p.GstRate, p.IsActive);
}
