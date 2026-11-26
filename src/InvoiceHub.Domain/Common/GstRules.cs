using System.Text.RegularExpressions;

namespace InvoiceHub.Domain.Common;

/// <summary>
/// Format rules for Indian GST identifiers.
/// </summary>
public static partial class GstRules
{
    /// <summary>GST slab rates, in percent.</summary>
    public static readonly IReadOnlySet<decimal> ValidRates = new HashSet<decimal> { 0m, 0.25m, 3m, 5m, 12m, 18m, 28m };

    /// <summary>Two-digit state code, for example "27" for Maharashtra.</summary>
    public static string StateCode(string? value)
    {
        var code = Guard.NotBlank(value, "State code", 2);
        if (!StateCodeRegex().IsMatch(code))
            throw new DomainException("State code must be two digits.");
        // GST state/UT codes run 01-38; 97 is "Other Territory". Codes such as 00 or 99 are not states.
        var number = int.Parse(code, System.Globalization.CultureInfo.InvariantCulture);
        if (number is < 1 or > 38 && number != 97)
            throw new DomainException($"{code} is not a GST state code (01-38 or 97).");

        return code;
    }

    /// <summary>15-character GSTIN. Its first two digits must match the given state code.</summary>
    public static string Gstin(string? value, string stateCode)
    {
        var gstin = Guard.NotBlank(value, "GSTIN", 15).ToUpperInvariant();
        if (!GstinRegex().IsMatch(gstin))
            throw new DomainException("GSTIN format is invalid.");
        if (!gstin.StartsWith(stateCode, StringComparison.Ordinal))
            throw new DomainException("GSTIN does not match the state code.");

        return gstin;
    }

    /// <summary>HSN or SAC code of 4, 6 or 8 digits.</summary>
    public static string HsnCode(string? value)
    {
        var code = Guard.NotBlank(value, "HSN code", 8);
        if (!HsnRegex().IsMatch(code))
            throw new DomainException("HSN code must be 4, 6 or 8 digits.");

        return code;
    }

    public static decimal Rate(decimal value)
    {
        if (!ValidRates.Contains(value))
            throw new DomainException($"{value}% is not a valid GST rate.");

        return value;
    }

    [GeneratedRegex(@"^\d{2}$")]
    private static partial Regex StateCodeRegex();

    [GeneratedRegex(@"^\d{2}[A-Z]{5}\d{4}[A-Z][1-9A-Z]Z[0-9A-Z]$")]
    private static partial Regex GstinRegex();

    [GeneratedRegex(@"^(\d{4}|\d{6}|\d{8})$")]
    private static partial Regex HsnRegex();
}
