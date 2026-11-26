using FluentAssertions;
using InvoiceHub.Domain.Common;

namespace InvoiceHub.UnitTests.Domain;

public sealed class GstRulesStateCodeTests
{
    [Theory]
    [InlineData("01")]
    [InlineData("07")]
    [InlineData("24")]
    [InlineData("27")]
    [InlineData("38")]
    [InlineData("97")]
    public void Real_state_and_territory_codes_are_accepted(string code) =>
        GstRules.StateCode(code).Should().Be(code);

    [Theory]
    [InlineData("00")]
    [InlineData("39")]
    [InlineData("96")]
    [InlineData("99")]
    public void Two_digits_that_are_not_a_gst_state_are_rejected(string code)
    {
        var act = () => GstRules.StateCode(code);

        act.Should().Throw<DomainException>().WithMessage("*not a GST state code*");
    }
}
