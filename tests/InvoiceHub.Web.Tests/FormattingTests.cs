using InvoiceHub.Web.Components.Shared;
using InvoiceHub.Web.Services;

namespace InvoiceHub.Web.Tests;

public sealed class FormattingTests
{
    [Theory]
    [InlineData(0, "₹0.00")]
    [InlineData(999.5, "₹999.50")]
    [InlineData(1000, "₹1,000.00")]
    [InlineData(123456, "₹1,23,456.00")]
    [InlineData(1234567.891, "₹12,34,567.89")]
    [InlineData(123456789, "₹12,34,56,789.00")]
    [InlineData(-4500, "-₹4,500.00")]
    public void Money_uses_indian_digit_grouping(double amount, string expected) =>
        Assert.Equal(expected, Money.Format((decimal)amount));

    [Fact]
    public void Money_can_leave_out_the_symbol() => Assert.Equal("1,180.00", Money.Format(1180m, symbol: false));

    [Theory]
    [InlineData(950, "950")]
    [InlineData(12500, "12.5k")]
    [InlineData(250000, "2.5L")]
    [InlineData(30000000, "3Cr")]
    public void Compact_uses_lakh_and_crore(double amount, string expected) =>
        Assert.Equal(expected, Money.Compact((decimal)amount));

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(870, 1000)]
    [InlineData(1180, 2000)]
    [InlineData(2300, 2500)]
    [InlineData(4100, 5000)]
    [InlineData(80000, 100000)]
    public void Chart_axis_tops_out_at_a_round_number(double value, double expected) =>
        Assert.Equal((decimal)expected, RevenueChart.NiceMax((decimal)value));
}
