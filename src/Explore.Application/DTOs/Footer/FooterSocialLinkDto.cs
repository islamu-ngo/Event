namespace Explore.Application.DTOs.Footer;

public sealed record FooterSocialLinkDto
{
    /// <summary>Platform identifier key (e.g. "twitter", "facebook", "instagram", "linkedin", "youtube").</summary>
    public string Platform { get; init; } = string.Empty;

    /// <summary>Absolute URL to the social media profile page.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>Accessible label for the icon button (e.g. "Follow us on Twitter").</summary>
    public string Label { get; init; } = string.Empty;
}
