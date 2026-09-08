using System.Globalization;

using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Helpers;

public static class TicketPriceSummaryFormatter
{
    public static string? Format(EventDto? eventDto) => eventDto is null
        ? null
        : Format(
            eventDto.TicketPriceSummary?.SummaryCode,
            eventDto.TicketPriceSummary?.CurrencyCode,
            eventDto.TicketPriceSummary?.CurrencyMinorUnitDigits,
            eventDto.TicketPriceSummary?.FromAmountMinor);

    public static string? Format(EventListDto? eventDto) => eventDto is null
        ? null
        : Format(
            eventDto.TicketPriceSummary?.SummaryCode,
            eventDto.TicketPriceSummary?.CurrencyCode,
            eventDto.TicketPriceSummary?.CurrencyMinorUnitDigits,
            eventDto.TicketPriceSummary?.FromAmountMinor);

    public static string? Format(
        string? summaryCode,
        string? currencyCode,
        int? currencyMinorUnitDigits,
        long? fromAmountMinor)
    {
        var hasPositiveAmount = fromAmountMinor is > 0;
        var money = hasPositiveAmount
            ? FormatMoney(fromAmountMinor!.Value, currencyCode, currencyMinorUnitDigits.GetValueOrDefault())
            : null;

        return summaryCode?.Trim().ToUpperInvariant() switch
        {
            "FREE" => "Free",
            "FIXED" when money is not null => $"From {money}",
            "DONATION" when money is not null => $"Donation from {money}",
            "DONATION" => "Donation",
            "PAY_WHAT_YOU_CAN" when money is not null => $"Pay what you can from {money}",
            "PAY_WHAT_YOU_CAN" => "Pay what you can",
            "SLIDING_SCALE" when money is not null => $"Sliding scale from {money}",
            "SLIDING_SCALE" => "Sliding scale",
            "MIXED_WITH_FREE" => "Free and other ticket options",
            "MIXED" when money is not null => $"From {money}",
            "MIXED" => "Flexible pricing",
            _ => null
        };
    }

    private static string FormatMoney(long amountMinor, string? currencyCode, int minorUnitDigits)
    {
        var digits = Math.Max(0, minorUnitDigits);
        var amount = amountMinor.ToString(CultureInfo.InvariantCulture);

        if (digits > 0)
        {
            amount = amount.PadLeft(digits + 1, '0');
            amount = $"{amount[..^digits]}.{amount[^digits..]}";
        }

        return string.IsNullOrWhiteSpace(currencyCode)
            ? amount
            : $"{currencyCode.Trim()} {amount}";
    }
}
