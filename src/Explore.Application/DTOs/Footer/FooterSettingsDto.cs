namespace Explore.Application.DTOs.Footer;

public sealed record FooterSettingsDto
{
    public bool Enabled { get; init; }
    public string Template { get; init; } = "standard-3-col";
    public bool ShowDescription { get; init; }
    public string DescriptionText { get; init; } = string.Empty;
    public bool ShowSocialLinks { get; init; }
    public IReadOnlyList<FooterSocialLinkDto> SocialLinks { get; init; } = [];
    public string CopyrightText { get; init; } = string.Empty;
    public bool ShowCookieSettingsLink { get; init; }
}
