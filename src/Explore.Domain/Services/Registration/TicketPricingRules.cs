using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Explore.Domain.Services.Registration;

public static class TicketPricingRules
{
    public static void ValidateConfiguration(
        TicketPricingModeEnum pricingMode,
        string currencyCode,
        long? fixedPriceMinor,
        long? minimumPriceMinor,
        long? suggestedPriceMinor)
    {
        CurrencyMetadata currency = CurrencyMetadata.Get(currencyCode);

        if (!Enum.IsDefined(pricingMode))
        {
            throw new ArgumentOutOfRangeException(nameof(pricingMode));
        }

        if (new[] { fixedPriceMinor, minimumPriceMinor, suggestedPriceMinor }.Any(static amount => amount < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(fixedPriceMinor), "Ticket price minor units cannot be negative.");
        }

        if (currency.IsNoCurrency && pricingMode != TicketPricingModeEnum.Free)
        {
            throw new ArgumentException("XXX currency is allowed only for free ticket catalogs.", nameof(currencyCode));
        }

        switch (pricingMode)
        {
            case TicketPricingModeEnum.Fixed when fixedPriceMinor is null || fixedPriceMinor <= 0 || minimumPriceMinor is not null || suggestedPriceMinor is not null:
                throw new ArgumentException("Fixed pricing requires one positive minor-unit price.");

            case TicketPricingModeEnum.Free when fixedPriceMinor is not null || minimumPriceMinor is not null || suggestedPriceMinor is not null:
                throw new ArgumentException("Free pricing does not allow price amounts.");

            case TicketPricingModeEnum.Donation when fixedPriceMinor is not null || suggestedPriceMinor is not null:
                throw new ArgumentException("Donation pricing allows only an optional minimum amount.");

            case TicketPricingModeEnum.PayWhatYouCan when fixedPriceMinor is not null || (minimumPriceMinor is not null && suggestedPriceMinor is not null && suggestedPriceMinor < minimumPriceMinor):
                throw new ArgumentException("Pay-what-you-can pricing allows optional ordered minimum and suggested amounts.");

            case TicketPricingModeEnum.SlidingScale when fixedPriceMinor is not null || minimumPriceMinor is null || suggestedPriceMinor is null || suggestedPriceMinor < minimumPriceMinor:
                throw new ArgumentException("Sliding-scale pricing requires ordered minimum and suggested amounts.");
        }
    }

    public static long ValidateChosenUnitPriceMinor(
        TicketPricingModeEnum pricingMode,
        string currencyCode,
        long chosenUnitPriceMinor,
        long? minimumPriceMinor)
    {
        if (chosenUnitPriceMinor < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chosenUnitPriceMinor));
        }

        if (pricingMode is TicketPricingModeEnum.Fixed or TicketPricingModeEnum.Free || !Enum.IsDefined(pricingMode))
        {
            throw new ArgumentException("Only buyer-priced ticket modes accept a chosen amount.", nameof(pricingMode));
        }

        CurrencyMetadata currency = CurrencyMetadata.Get(currencyCode);
        if (currency.IsNoCurrency)
        {
            throw new ArgumentException("XXX currency cannot be used for buyer-chosen pricing.", nameof(currencyCode));
        }

        if (chosenUnitPriceMinor < minimumPriceMinor.GetValueOrDefault())
        {
            throw new ArgumentOutOfRangeException(nameof(chosenUnitPriceMinor), "Chosen price is below the ticket minimum.");
        }

        return chosenUnitPriceMinor;
    }
}
