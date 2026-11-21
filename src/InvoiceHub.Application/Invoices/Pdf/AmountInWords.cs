namespace InvoiceHub.Application.Invoices.Pdf;

/// <summary>Spells a rupee amount in words using the Indian system (thousand, lakh, crore), as printed on GST invoices.</summary>
public static class AmountInWords
{
    private static readonly string[] Small =
    [
        "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven", "Twelve",
        "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen",
    ];

    private static readonly string[] Tens = ["", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"];

    public static string ToIndianRupees(decimal amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount cannot be negative.");

        var rounded = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        var rupees = (long)decimal.Truncate(rounded);
        var paise = (int)((rounded - rupees) * 100m);

        var rupeeText = $"{Words(rupees)} {(rupees == 1 ? "Rupee" : "Rupees")}";
        return paise == 0
            ? $"{rupeeText} Only"
            : $"{rupeeText} and {Words(paise)} {(paise == 1 ? "Paisa" : "Paise")} Only";
    }

    public static string ToIndianRupees(long rupees)
    {
        if (rupees < 0)
            throw new ArgumentOutOfRangeException(nameof(rupees), "Amount cannot be negative.");
        return $"{Words(rupees)} {(rupees == 1 ? "Rupee" : "Rupees")} Only";
    }

    private static string Words(long n)
    {
        if (n < 20) return Small[n];
        if (n < 100) return n % 10 == 0 ? Tens[n / 10] : $"{Tens[n / 10]} {Small[n % 10]}";
        if (n < 1_000) return Join(n / 100, "Hundred", n % 100);
        if (n < 100_000) return Join(n / 1_000, "Thousand", n % 1_000);
        if (n < 10_000_000) return Join(n / 100_000, "Lakh", n % 100_000);
        return Join(n / 10_000_000, "Crore", n % 10_000_000);
    }

    private static string Join(long count, string unit, long rest) =>
        rest == 0 ? $"{Words(count)} {unit}" : $"{Words(count)} {unit} {Words(rest)}";
}
