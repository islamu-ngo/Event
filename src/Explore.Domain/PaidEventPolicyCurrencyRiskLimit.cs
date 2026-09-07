using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public sealed record PaidCheckoutReservedExposure(
    string CurrencyCode,
    long PerEventAmountMinor,
    int PerEventCount,
    long RollingOrganizerAmountMinor,
    int RollingOrganizerCount);

public sealed class PaidEventPolicyCurrencyRiskLimit
{
    private PaidEventPolicyCurrencyRiskLimit(
        string currencyCode,
        long? perEventSalesCeilingMinor,
        int? perEventSalesCountCeiling,
        long? rollingOrganizerSalesCeilingMinor,
        int? rollingOrganizerSalesCountCeiling,
        int? rollingOrganizerWindowDays,
        long? highValueReviewThresholdMinor)
    {
        CurrencyCode = currencyCode;
        PerEventSalesCeilingMinor = perEventSalesCeilingMinor;
        PerEventSalesCountCeiling = perEventSalesCountCeiling;
        RollingOrganizerSalesCeilingMinor = rollingOrganizerSalesCeilingMinor;
        RollingOrganizerSalesCountCeiling = rollingOrganizerSalesCountCeiling;
        RollingOrganizerWindowDays = rollingOrganizerWindowDays;
        HighValueReviewThresholdMinor = highValueReviewThresholdMinor;
    }

    public string CurrencyCode { get; }
    public long? PerEventSalesCeilingMinor { get; }
    public int? PerEventSalesCountCeiling { get; }
    public long? RollingOrganizerSalesCeilingMinor { get; }
    public int? RollingOrganizerSalesCountCeiling { get; }
    public int? RollingOrganizerWindowDays { get; }
    public long? HighValueReviewThresholdMinor { get; }

    public static PaidEventPolicyCurrencyRiskLimit Create(
        string currencyCode,
        long? perEventSalesCeilingMinor,
        int? perEventSalesCountCeiling,
        long? rollingOrganizerSalesCeilingMinor,
        int? rollingOrganizerSalesCountCeiling,
        int? rollingOrganizerWindowDays,
        long? highValueReviewThresholdMinor)
    {
        CurrencyMetadata currency = CurrencyMetadata.Get(currencyCode);
        if (currency.IsNoCurrency)
        {
            throw new ArgumentException("Currency risk limits require a monetary currency.", nameof(currencyCode));
        }

        ValidatePositive(perEventSalesCeilingMinor, nameof(perEventSalesCeilingMinor));
        ValidatePositive(perEventSalesCountCeiling, nameof(perEventSalesCountCeiling));
        ValidatePositive(rollingOrganizerSalesCeilingMinor, nameof(rollingOrganizerSalesCeilingMinor));
        ValidatePositive(rollingOrganizerSalesCountCeiling, nameof(rollingOrganizerSalesCountCeiling));
        ValidatePositive(rollingOrganizerWindowDays, nameof(rollingOrganizerWindowDays));
        ValidatePositive(highValueReviewThresholdMinor, nameof(highValueReviewThresholdMinor));
        bool hasRolling = rollingOrganizerSalesCeilingMinor.HasValue || rollingOrganizerSalesCountCeiling.HasValue;
        if (hasRolling != rollingOrganizerWindowDays.HasValue)
        {
            throw new ArgumentException("Rolling organizer ceilings and their explicit window must be configured together.");
        }

        return new(currency.Code, perEventSalesCeilingMinor, perEventSalesCountCeiling,
            rollingOrganizerSalesCeilingMinor, rollingOrganizerSalesCountCeiling, rollingOrganizerWindowDays,
            highValueReviewThresholdMinor);
    }

    public bool WouldExceed(PaidCheckoutReservedExposure exposure, long candidateAmountMinor)
    {
        ArgumentNullException.ThrowIfNull(exposure);
        if (!string.Equals(CurrencyCode, exposure.CurrencyCode, StringComparison.Ordinal) || candidateAmountMinor <= 0 ||
            exposure.PerEventAmountMinor < 0 || exposure.PerEventCount < 0 || exposure.RollingOrganizerAmountMinor < 0 ||
            exposure.RollingOrganizerCount < 0)
        {
            throw new ArgumentException("Exposure must be non-negative and use the exact policy currency.", nameof(exposure));
        }

        long eventAmount = checked(exposure.PerEventAmountMinor + candidateAmountMinor);
        long organizerAmount = checked(exposure.RollingOrganizerAmountMinor + candidateAmountMinor);
        int eventCount = checked(exposure.PerEventCount + 1);
        int organizerCount = checked(exposure.RollingOrganizerCount + 1);
        return PerEventSalesCeilingMinor is { } eventAmountLimit && eventAmount > eventAmountLimit ||
               PerEventSalesCountCeiling is { } eventCountLimit && eventCount > eventCountLimit ||
               RollingOrganizerSalesCeilingMinor is { } organizerAmountLimit && organizerAmount > organizerAmountLimit ||
               RollingOrganizerSalesCountCeiling is { } organizerCountLimit && organizerCount > organizerCountLimit;
    }

    private static void ValidatePositive(long? value, string parameterName)
    {
        if (value is <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ValidatePositive(int? value, string parameterName)
    {
        if (value is <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
