namespace InvoiceHub.Domain.Common;

/// <summary>Tax on one invoice line, already rounded to paise.</summary>
public readonly record struct GstAmounts(decimal Cgst, decimal Sgst, decimal Igst)
{
    public decimal Total => Cgst + Sgst + Igst;
}

/// <summary>
/// Indian GST split. Supplier and place of supply in the same state: CGST + SGST, each half the rate.
/// Different states: IGST at the full rate. Every amount is rounded to 2 decimals (half away from zero)
/// per line, so invoice totals are plain sums of already-rounded line amounts.
/// </summary>
public static class GstCalculator
{
    public static GstAmounts Calculate(decimal taxableValue, decimal gstRate, bool isIntraState)
    {
        if (taxableValue < 0)
            throw new DomainException("Taxable value cannot be negative.");
        GstRules.Rate(gstRate);

        if (isIntraState)
        {
            var half = Round(taxableValue * gstRate / 200m);
            return new GstAmounts(half, half, 0m);
        }

        return new GstAmounts(0m, 0m, Round(taxableValue * gstRate / 100m));
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
