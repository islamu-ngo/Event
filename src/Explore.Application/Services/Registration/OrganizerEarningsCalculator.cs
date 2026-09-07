using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.ValueObjects;

namespace Explore.Application.Services.Registration;

public sealed class OrganizerEarningsCalculator : IOrganizerEarningsCalculator
{
    public OrganizerEarnings Calculate(string currencyCode, long organizerDirectedTotalMinor, PlatformFeePolicy? platformFeePolicy)
    {
        CurrencyMetadata currency = CurrencyMetadata.Get(currencyCode);
        if (organizerDirectedTotalMinor < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(organizerDirectedTotalMinor));
        }

        if (currency.IsNoCurrency)
        {
            if (organizerDirectedTotalMinor > 0)
            {
                throw new ArgumentException("XXX currency cannot carry organizer-directed money.", nameof(currencyCode));
            }

            return new OrganizerEarnings(0, 0, 0, null);
        }

        long platformFeeMinor = platformFeePolicy?.CalculateFeeMinor(currency.Code, organizerDirectedTotalMinor) ?? 0;
        long organizerEarningsMinor = organizerDirectedTotalMinor - platformFeeMinor;
        int? policyVersionSnapshot = platformFeeMinor > 0 ? platformFeePolicy?.VersionNumber : null;

        return new OrganizerEarnings(
            organizerDirectedTotalMinor,
            platformFeeMinor,
            organizerEarningsMinor,
            policyVersionSnapshot);
    }
}
