using Explore.Domain;

namespace Explore.Application.Contracts.Services;

public interface IOrganizerEarningsCalculator
{
    OrganizerEarnings Calculate(string currencyCode, long organizerDirectedTotalMinor, PlatformFeePolicy? platformFeePolicy);
}

public sealed record OrganizerEarnings(
    long OrganizerDirectedTotalMinor,
    long PlatformFeeMinor,
    long OrganizerEarningsMinor,
    int? PlatformFeePolicyVersionSnapshot);
