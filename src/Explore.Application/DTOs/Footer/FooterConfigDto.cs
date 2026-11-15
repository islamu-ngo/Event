using Explore.Application.DTOs.PublicExperience;

namespace Explore.Application.DTOs.Footer;

public sealed record FooterConfigDto
{
    public Guid? TenantId { get; init; }
    public string Revision { get; init; } = string.Empty;
    public string BrandDisplayName { get; init; } = string.Empty;
    public string BrandLogoUrl { get; init; } = string.Empty;
    public bool ShowCommunityGuidelinesLink { get; init; }
    public TenantDirectoryOperatorPublicDto? DirectoryOperator { get; init; }
    public InstanceOperatorPublicDto? InstanceOperator { get; init; }
    public FooterSettingsDto Settings { get; init; } = new();
    public IReadOnlyList<FooterLinkGroupDto> LinkGroups { get; init; } = [];
}
