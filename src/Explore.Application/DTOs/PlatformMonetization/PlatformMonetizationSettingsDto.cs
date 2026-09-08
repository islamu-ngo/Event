namespace Explore.Application.DTOs.PlatformMonetization;

public sealed record PlatformMonetizationSettingsDto
{
    public bool FeeEnabled { get; init; }
    public int FeeBasisPoints { get; init; }
    public IReadOnlyList<PlatformFeeFixedChargeDto> FixedCharges { get; init; } = [];
    public int FeeVersion { get; init; }
    public bool ContributionEnabled { get; init; }
    public string ContributionHeading { get; init; } = string.Empty;
    public string ContributionBody { get; init; } = string.Empty;
    public IReadOnlyList<PlatformContributionOptionDto> ContributionOptions { get; init; } = [];
    public int ContributionVersion { get; init; }
}

public sealed record UpdatePlatformMonetizationSettingsDto
{
    public bool FeeEnabled { get; init; }
    public int FeeBasisPoints { get; init; }
    public IReadOnlyList<PlatformFeeFixedChargeDto> FixedCharges { get; init; } = [];
    public int ExpectedFeeVersion { get; init; }
    public bool ContributionEnabled { get; init; }
    public string ContributionHeading { get; init; } = string.Empty;
    public string ContributionBody { get; init; } = string.Empty;
    public IReadOnlyList<PlatformContributionOptionDto> ContributionOptions { get; init; } = [];
    public int ExpectedContributionVersion { get; init; }
}

public sealed record PlatformFeeFixedChargeDto
{
    public string CurrencyCode { get; init; } = string.Empty;
    public long AmountMinor { get; init; }
}

public sealed record PlatformContributionOptionDto
{
    public int ContributionBasisPoints { get; init; }
    public int SortOrder { get; init; }
    public bool IsDefault { get; init; }
}
