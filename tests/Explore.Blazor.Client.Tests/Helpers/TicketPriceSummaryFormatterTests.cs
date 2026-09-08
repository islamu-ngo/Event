using Explore.Blazor.Client.Helpers;

namespace Explore.Blazor.Client.Tests.Helpers;

public sealed class TicketPriceSummaryFormatterTests
{
    [Test]
    [Arguments("FREE", null, "Free")]
    [Arguments("DONATION", null, "Donation")]
    [Arguments("DONATION", 1250L, "Donation from EUR 12.50")]
    [Arguments("PAY_WHAT_YOU_CAN", null, "Pay what you can")]
    [Arguments("PAY_WHAT_YOU_CAN", 1250L, "Pay what you can from EUR 12.50")]
    [Arguments("SLIDING_SCALE", null, "Sliding scale")]
    [Arguments("SLIDING_SCALE", 1250L, "Sliding scale from EUR 12.50")]
    [Arguments("MIXED_WITH_FREE", null, "Free and other ticket options")]
    [Arguments("MIXED", 0L, "Flexible pricing")]
    [Arguments("MIXED", 1250L, "From EUR 12.50")]
    public async Task Format_ReturnsExpectedSummary(string code, long? amountMinor, string expected)
    {
        var result = TicketPriceSummaryFormatter.Format(code, "EUR", 2, amountMinor);

        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task Format_FixedPositiveAmountWithZeroDigits_PreservesWholeMinorUnits()
    {
        var result = TicketPriceSummaryFormatter.Format("FIXED", "JPY", 0, 1234);

        await Assert.That(result).IsEqualTo("From JPY 1234");
    }

    [Test]
    public async Task Format_FixedPositiveAmountWithThreeDigits_PreservesAllMinorUnits()
    {
        var result = TicketPriceSummaryFormatter.Format("FIXED", "KWD", 3, 12345);

        await Assert.That(result).IsEqualTo("From KWD 12.345");
    }

    [Test]
    public async Task Format_NullOrUnknownSummary_ReturnsNull()
    {
        await Assert.That(TicketPriceSummaryFormatter.Format(null, "EUR", 2, 100)).IsNull();
        await Assert.That(TicketPriceSummaryFormatter.Format("UNKNOWN", "EUR", 2, 100)).IsNull();
    }
}
