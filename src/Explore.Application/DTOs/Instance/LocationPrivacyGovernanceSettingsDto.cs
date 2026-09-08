namespace Explore.Application.DTOs.Instance;

public sealed record LocationPrivacyGovernanceSettingsDto
{
    public bool AllowHomeLocations { get; init; }
    public bool AllowPublicExactAddress { get; init; }
    public bool AllowPublicCoordinates { get; init; }
    public string MinimumHomeAudience { get; init; } = "NEVER";
    public string DefaultRevealOffset { get; init; } = "P30D";
}
