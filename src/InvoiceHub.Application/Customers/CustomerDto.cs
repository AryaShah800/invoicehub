using InvoiceHub.Domain.Customers;

namespace InvoiceHub.Application.Customers;

public sealed record CustomerDto(
    Guid Id,
    string Name,
    string Email,
    string? Gstin,
    string StateCode,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string PostalCode);

internal static class CustomerMapping
{
    public static CustomerDto ToDto(this Customer c) => new(
        c.Id, c.Name, c.Email, c.Gstin, c.StateCode,
        c.BillingAddress.Line1, c.BillingAddress.Line2, c.BillingAddress.City, c.BillingAddress.PostalCode);
}
