namespace Explore.Application.Models.PublicExperience;

public sealed record PublicExperienceCtasConfig(
    int SchemaVersion = 1,
    IReadOnlyList<PublicExperienceCtaConfig>? Ctas = null);

public sealed record PublicExperienceCtaConfig(
    string Id,
    string Label,
    string Url,
    PublicExperienceCtaPlacement Placement,
    PublicExperienceCtaStyle Style = PublicExperienceCtaStyle.Primary,
    int SortOrder = 0,
    bool IsEnabled = true);

public enum PublicExperienceCtaPlacement
{
    Header = 0,
    Hero = 1,
    HomeBlock = 2,
    Footer = 3
}

public enum PublicExperienceCtaStyle
{
    Primary = 0,
    Secondary = 1,
    Text = 2
}
