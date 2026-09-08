using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public sealed record RefundAllocation
{
    private RefundAllocation(
        long organizerAmountMinor,
        long platformFeeMinor,
        long platformContributionMinor)
    {
        OrganizerAmountMinor = organizerAmountMinor;
        PlatformFeeMinor = platformFeeMinor;
        PlatformContributionMinor = platformContributionMinor;
        TotalMinor = MinorUnitMath.Add(organizerAmountMinor, platformContributionMinor);
    }

    public long OrganizerAmountMinor { get; }
    public long PlatformFeeMinor { get; }
    public long PlatformContributionMinor { get; }
    public long TotalMinor { get; }

    public static RefundAllocation AllocatePartial(
        long requestedTotalMinor,
        long capturedOrganizerMinor,
        long capturedPlatformFeeMinor,
        long capturedContributionMinor)
    {
        if (requestedTotalMinor <= 0 || capturedOrganizerMinor < 0 || capturedPlatformFeeMinor < 0 ||
            capturedContributionMinor < 0 || capturedPlatformFeeMinor > capturedOrganizerMinor)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedTotalMinor));
        }

        long capturedTotalMinor = MinorUnitMath.Add(capturedOrganizerMinor, capturedContributionMinor);
        if (requestedTotalMinor > capturedTotalMinor || capturedTotalMinor == 0)
        {
            throw new InvalidOperationException("Refund amount exceeds the captured amount.");
        }

        (long organizerMinor, Int128 organizerRemainder) = Proportional(
            requestedTotalMinor, capturedOrganizerMinor, capturedTotalMinor);
        (long contributionMinor, Int128 contributionRemainder) = Proportional(
            requestedTotalMinor, capturedContributionMinor, capturedTotalMinor);

        long undistributed = requestedTotalMinor - MinorUnitMath.Add(organizerMinor, contributionMinor);
        if (undistributed == 1)
        {
            if (contributionRemainder > organizerRemainder)
            {
                contributionMinor = MinorUnitMath.Add(contributionMinor, 1);
            }
            else
            {
                organizerMinor = MinorUnitMath.Add(organizerMinor, 1);
            }
        }

        long feeMinor = capturedOrganizerMinor == 0
            ? 0
            : RoundRatio(organizerMinor, capturedPlatformFeeMinor, capturedOrganizerMinor);
        return new(organizerMinor, feeMinor, contributionMinor);
    }

    public static RefundAllocation AllocateReservationDelta(
        long previouslyReservedMinor,
        long requestedTotalMinor,
        long capturedOrganizerMinor,
        long capturedPlatformFeeMinor,
        long capturedContributionMinor,
        long allocatedOrganizerMinor,
        long allocatedPlatformFeeMinor,
        long allocatedContributionMinor)
    {
        if (previouslyReservedMinor < 0 || allocatedOrganizerMinor < 0 || allocatedPlatformFeeMinor < 0 ||
            allocatedContributionMinor < 0 ||
            allocatedOrganizerMinor > capturedOrganizerMinor ||
            allocatedPlatformFeeMinor > capturedPlatformFeeMinor ||
            allocatedContributionMinor > capturedContributionMinor ||
            previouslyReservedMinor != MinorUnitMath.Add(allocatedOrganizerMinor, allocatedContributionMinor))
        {
            throw new ArgumentOutOfRangeException(nameof(previouslyReservedMinor));
        }

        RefundAllocation after = AllocatePartial(
            MinorUnitMath.Add(previouslyReservedMinor, requestedTotalMinor),
            capturedOrganizerMinor,
            capturedPlatformFeeMinor,
            capturedContributionMinor);
        long organizerMinor = checked(after.OrganizerAmountMinor - allocatedOrganizerMinor);
        long contributionMinor = checked(after.PlatformContributionMinor - allocatedContributionMinor);
        if (organizerMinor < 0)
        {
            organizerMinor = 0;
            contributionMinor = requestedTotalMinor;
        }
        else if (contributionMinor < 0)
        {
            organizerMinor = requestedTotalMinor;
            contributionMinor = 0;
        }
        long feeMinor = Math.Min(
            organizerMinor,
            Math.Max(0, checked(after.PlatformFeeMinor - allocatedPlatformFeeMinor)));
        return new(organizerMinor, feeMinor, contributionMinor);
    }

    private static (long Quotient, Int128 Remainder) Proportional(long amount, long component, long total)
    {
        Int128 numerator = (Int128)amount * component;
        return (checked((long)(numerator / total)), numerator % total);
    }

    private static long RoundRatio(long amount, long numeratorFactor, long denominator) =>
        checked((long)(((Int128)amount * numeratorFactor + (denominator / 2)) / denominator));
}
