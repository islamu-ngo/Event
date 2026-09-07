using Explore.Application.DTOs.Event;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Explore.Application.Services;

public static class EventTicketPriceSummaryMapper
{
    public static EventTicketPriceSummaryDto? Map(Event @event)
    {
        if (@event.ParticipationConfiguration?.ParticipationHandlingModeId != (int)ParticipationHandlingModeEnum.PlatformManaged)
        {
            return null;
        }

        EventTicketCatalogVersion? catalog = @event.TicketCatalogVersions.SingleOrDefault(value =>
            !value.IsDeleted && value.TicketCatalogStatusId == (int)TicketCatalogStatusEnum.Published);
        EventTicketType[] ticketTypes = catalog?.TicketTypes.Where(value => !value.IsDeleted).ToArray() ?? [];
        if (ticketTypes.Length == 0)
        {
            return null;
        }

        CurrencyMetadata currency = CurrencyMetadata.Get(catalog!.CurrencyCode);
        long[] amounts = ticketTypes.Select(SelectableAmountMinor).ToArray();
        bool hasFreeTicket = ticketTypes.Any(value =>
            value.TicketPricingModeId == (int)TicketPricingModeEnum.Free);
        bool homogeneous = ticketTypes.Select(value => value.TicketPricingModeId).Distinct().Count() == 1;
        string summaryCode = homogeneous
            ? SummaryCodeFor((TicketPricingModeEnum)ticketTypes[0].TicketPricingModeId)
            : hasFreeTicket ? "MIXED_WITH_FREE" : "MIXED";

        return new EventTicketPriceSummaryDto
        {
            SummaryCode = summaryCode,
            CurrencyCode = currency.IsNoCurrency ? null : currency.Code,
            CurrencyMinorUnitDigits = currency.MinorUnitDigits,
            FromAmountMinor = amounts.Min()
        };
    }

    private static long SelectableAmountMinor(EventTicketType ticketType) =>
        (TicketPricingModeEnum)ticketType.TicketPricingModeId switch
        {
            TicketPricingModeEnum.Free => 0,
            TicketPricingModeEnum.Fixed => ticketType.FixedPriceMinor!.Value,
            TicketPricingModeEnum.Donation or TicketPricingModeEnum.PayWhatYouCan => ticketType.MinimumPriceMinor ?? 0,
            TicketPricingModeEnum.SlidingScale => ticketType.MinimumPriceMinor!.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(ticketType))
        };

    private static string SummaryCodeFor(TicketPricingModeEnum pricingMode) => pricingMode switch
    {
        TicketPricingModeEnum.Free => "FREE",
        TicketPricingModeEnum.Fixed => "FIXED",
        TicketPricingModeEnum.Donation => "DONATION",
        TicketPricingModeEnum.PayWhatYouCan => "PAY_WHAT_YOU_CAN",
        TicketPricingModeEnum.SlidingScale => "SLIDING_SCALE",
        _ => throw new ArgumentOutOfRangeException(nameof(pricingMode))
    };
}
