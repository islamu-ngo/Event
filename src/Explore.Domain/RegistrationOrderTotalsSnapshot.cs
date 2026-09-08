using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public sealed record RegistrationOrderTotalsSnapshot
{
    private RegistrationOrderTotalsSnapshot(
        string currencyCode,
        long organizerDirectedTotalMinor,
        long platformFeeTotalMinor,
        long organizerEarningsTotalMinor,
        long platformContributionTotalMinor)
    {
        CurrencyCode = currencyCode;
        OrganizerDirectedTotalMinor = organizerDirectedTotalMinor;
        PlatformFeeTotalMinor = platformFeeTotalMinor;
        OrganizerEarningsTotalMinor = organizerEarningsTotalMinor;
        PlatformContributionTotalMinor = platformContributionTotalMinor;
        TotalDueMinor = MinorUnitMath.Add(organizerDirectedTotalMinor, platformContributionTotalMinor);
    }

    public string CurrencyCode { get; }

    public long OrganizerDirectedTotalMinor { get; }

    public long PlatformFeeTotalMinor { get; }

    public long OrganizerEarningsTotalMinor { get; }

    public long PlatformContributionTotalMinor { get; }

    public long TotalDueMinor { get; }

    public static RegistrationOrderTotalsSnapshot Create(
        string currencyCode,
        long organizerDirectedTotalMinor,
        long platformFeeTotalMinor,
        long organizerEarningsTotalMinor,
        long platformContributionTotalMinor)
    {
        CurrencyMetadata currency = CurrencyMetadata.Get(currencyCode);
        if (organizerDirectedTotalMinor < 0 || platformFeeTotalMinor < 0 || organizerEarningsTotalMinor < 0 || platformContributionTotalMinor < 0 ||
            platformFeeTotalMinor > organizerDirectedTotalMinor ||
            organizerEarningsTotalMinor != organizerDirectedTotalMinor - platformFeeTotalMinor ||
            (currency.IsNoCurrency && (organizerDirectedTotalMinor > 0 || platformContributionTotalMinor > 0)))
        {
            throw new ArgumentException("Order totals are not a valid minor-unit composition.");
        }

        return new RegistrationOrderTotalsSnapshot(
            currency.Code,
            organizerDirectedTotalMinor,
            platformFeeTotalMinor,
            organizerEarningsTotalMinor,
            platformContributionTotalMinor);
    }
}
