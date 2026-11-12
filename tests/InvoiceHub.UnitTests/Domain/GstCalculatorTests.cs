using InvoiceHub.Domain.Common;

namespace InvoiceHub.UnitTests.Domain;

public class GstCalculatorTests
{
    [Fact]
    public void Same_state_splits_the_tax_equally_into_CGST_and_SGST()
    {
        var tax = GstCalculator.Calculate(1000m, 18m, isIntraState: true);

        Assert.Equal(90m, tax.Cgst);
        Assert.Equal(90m, tax.Sgst);
        Assert.Equal(0m, tax.Igst);
        Assert.Equal(180m, tax.Total);
    }

    [Fact]
    public void Different_state_charges_IGST_only()
    {
        var tax = GstCalculator.Calculate(1000m, 18m, isIntraState: false);

        Assert.Equal(0m, tax.Cgst);
        Assert.Equal(0m, tax.Sgst);
        Assert.Equal(180m, tax.Igst);
        Assert.Equal(180m, tax.Total);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 50)]
    [InlineData(12, 120)]
    [InlineData(28, 280)]
    public void Tax_follows_the_slab_rate(decimal rate, decimal expectedTotal)
    {
        Assert.Equal(expectedTotal, GstCalculator.Calculate(1000m, rate, false).Igst);
        Assert.Equal(expectedTotal, GstCalculator.Calculate(1000m, rate, true).Total);
    }

    [Fact]
    public void CGST_and_SGST_are_each_rounded_to_paise_half_away_from_zero()
    {
        // 18% of 10.10 = 1.818 -> IGST 1.82. Half is 0.909 -> 0.91 each.
        var intra = GstCalculator.Calculate(10.10m, 18m, true);
        var inter = GstCalculator.Calculate(10.10m, 18m, false);

        Assert.Equal(0.91m, intra.Cgst);
        Assert.Equal(0.91m, intra.Sgst);
        Assert.Equal(1.82m, inter.Igst);
    }

    [Fact]
    public void Midpoints_round_away_from_zero()
    {
        // 5% of 0.50 = 0.025 -> 0.03 (not banker's 0.02).
        Assert.Equal(0.03m, GstCalculator.Calculate(0.50m, 5m, false).Igst);
    }

    [Fact]
    public void Zero_taxable_value_has_zero_tax()
    {
        Assert.Equal(0m, GstCalculator.Calculate(0m, 18m, true).Total);
    }

    [Fact]
    public void Negative_taxable_value_is_rejected()
    {
        Assert.Throws<DomainException>(() => GstCalculator.Calculate(-1m, 18m, true));
    }

    [Fact]
    public void Invalid_rate_is_rejected()
    {
        Assert.Throws<DomainException>(() => GstCalculator.Calculate(100m, 7m, true));
    }
}
