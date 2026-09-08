namespace Explore.Application.DTOs.Footer;

public sealed record FooterConfigDto
{
    public FooterSettingsDto Settings { get; init; } = new();
    public IReadOnlyList<FooterLinkGroupDto> LinkGroups { get; init; } = [];
}
