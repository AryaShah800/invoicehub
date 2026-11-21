using InvoiceHub.Application.Invoices.Pdf;

namespace InvoiceHub.UnitTests.Invoices;

public sealed class AmountInWordsTests
{
    [Theory]
    [InlineData(0, "Zero Rupees Only")]
    [InlineData(1, "One Rupee Only")]
    [InlineData(21, "Twenty One Rupees Only")]
    [InlineData(100, "One Hundred Rupees Only")]
    [InlineData(1180, "One Thousand One Hundred Eighty Rupees Only")]
    [InlineData(100000, "One Lakh Rupees Only")]
    [InlineData(1234567, "Twelve Lakh Thirty Four Thousand Five Hundred Sixty Seven Rupees Only")]
    [InlineData(25000000, "Two Crore Fifty Lakh Rupees Only")]
    public void Whole_rupees_use_the_indian_numbering_system(long rupees, string expected) =>
        Assert.Equal(expected, AmountInWords.ToIndianRupees(rupees));

    [Theory]
    [InlineData("0.50", "Zero Rupees and Fifty Paise Only")]
    [InlineData("1.01", "One Rupee and One Paisa Only")]
    [InlineData("1180.25", "One Thousand One Hundred Eighty Rupees and Twenty Five Paise Only")]
    public void Paise_are_spelled_out(string amount, string expected) =>
        Assert.Equal(expected, AmountInWords.ToIndianRupees(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Negative_amounts_are_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AmountInWords.ToIndianRupees(-1m));
}
