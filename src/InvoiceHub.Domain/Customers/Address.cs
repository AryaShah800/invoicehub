using InvoiceHub.Domain.Common;

namespace InvoiceHub.Domain.Customers;

public sealed record Address
{
    private Address()
    {
        Line1 = null!;
        City = null!;
        PostalCode = null!;
    }

    public Address(string line1, string? line2, string city, string postalCode)
    {
        Line1 = Guard.NotBlank(line1, "Address line 1", 200);
        Line2 = string.IsNullOrWhiteSpace(line2) ? null : Guard.NotBlank(line2, "Address line 2", 200);
        City = Guard.NotBlank(city, "City", 100);
        PostalCode = Guard.NotBlank(postalCode, "Postal code", 10);
    }

    public string Line1 { get; private init; }
    public string? Line2 { get; private init; }
    public string City { get; private init; }
    public string PostalCode { get; private init; }
}
