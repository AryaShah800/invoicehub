using System.Globalization;
using System.Text;

namespace InvoiceHub.Web.Services;

/// <summary>Formatting that does not depend on the host's ICU data, so a slim container prints the same as a laptop.</summary>
public static class Money
{
    /// <summary>Rupees with Indian digit grouping, for example 12,34,567.50.</summary>
    public static string Format(decimal amount, bool symbol = true)
    {
        var negative = amount < 0;
        var fixedPoint = Math.Abs(amount).ToString("0.00", CultureInfo.InvariantCulture);
        var dot = fixedPoint.IndexOf('.');
        var whole = fixedPoint[..dot];
        var fraction = fixedPoint[dot..];

        var grouped = new StringBuilder();
        if (whole.Length > 3)
        {
            var head = whole[..^3];
            for (var i = 0; i < head.Length; i++)
            {
                if (i > 0 && (head.Length - i) % 2 == 0) grouped.Append(',');
                grouped.Append(head[i]);
            }
            grouped.Append(',').Append(whole[^3..]);
        }
        else grouped.Append(whole);

        return (negative ? "-" : "") + (symbol ? "₹" : "") + grouped + fraction;
    }

    /// <summary>Whole rupees, compact for chart axes: 1.2L (lakh) or 3.4Cr (crore).</summary>
    public static string Compact(decimal amount) => amount switch
    {
        >= 10_000_000m => $"{amount / 10_000_000m:0.#}Cr",
        >= 100_000m => $"{amount / 100_000m:0.#}L",
        >= 1_000m => $"{amount / 1_000m:0.#}k",
        _ => amount.ToString("0", CultureInfo.InvariantCulture),
    };

    public static string Date(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    public static string Month(int year, int month) => new DateOnly(year, month, 1).ToString("MMM", CultureInfo.InvariantCulture);
}
