using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public sealed class PlatformContributionOption
{
    private PlatformContributionOption()
    {
    }

    private PlatformContributionOption(int contributionBasisPoints, int sortOrder, bool isDefault)
    {
        Id = Guid.CreateVersion7();
        ContributionBasisPoints = contributionBasisPoints;
        SortOrder = sortOrder;
        IsDefault = isDefault;
    }

    public Guid Id { get; private set; }

    public int ContributionBasisPoints { get; private set; }

    public int SortOrder { get; private set; }

    public bool IsDefault { get; private set; }

    public static PlatformContributionOption Create(int contributionBasisPoints, int sortOrder, bool isDefault)
    {
        if (contributionBasisPoints is < 0 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(contributionBasisPoints));
        }

        if (sortOrder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sortOrder));
        }

        return new PlatformContributionOption(contributionBasisPoints, sortOrder, isDefault);
    }

    public long CalculateAmountMinor(long orderTotalMinor) => MinorUnitMath.ApplyBasisPoints(orderTotalMinor, ContributionBasisPoints);

    internal PlatformContributionOption Clone() => new(ContributionBasisPoints, SortOrder, IsDefault);
}
