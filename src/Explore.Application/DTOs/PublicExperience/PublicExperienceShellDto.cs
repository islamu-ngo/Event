using Explore.Application.DTOs.Footer;
using Explore.Application.Models;
using Explore.Application.Models.PublicExperience;

namespace Explore.Application.DTOs.PublicExperience;

public sealed record PublicExperienceShellDto
{
    private IReadOnlyList<PublicExperienceEventSectionDto> _eventSections =
        Array.AsReadOnly(Array.Empty<PublicExperienceEventSectionDto>());
    private IReadOnlyList<PublicExperienceCtaDto> _ctas = Array.AsReadOnly(Array.Empty<PublicExperienceCtaDto>());

    public int SchemaVersion { get; init; } = 1;
    public bool IsAvailable { get; init; }
    public string? UnavailableCode { get; init; }
    public TenantDirectoryOperatorPublicDto? DirectoryOperator { get; init; }
    public InstanceOperatorPublicDto? InstanceOperator { get; init; }
    public string Revision { get; init; } = string.Empty;
    public PublicExperienceMode Mode { get; init; } = PublicExperienceMode.DiscoveryCentric;
    public string RailPublicVisibility { get; init; } = "AuthenticatedOnly";
    public PublicExperienceHomeDto Home { get; init; } = new();
    public PublicExperienceNavigationDto Navigation { get; init; } = new();
    public PublicExperienceEventCatalogDto EventCatalog { get; init; } = new();
    public PublicExperiencePrimaryOrganizationDto PrimaryOrganization { get; init; } = new();
    public IReadOnlyList<PublicExperienceEventSectionDto> EventSections
    {
        get => _eventSections;
        init => _eventSections = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }

    public IReadOnlyList<PublicExperienceCtaDto> Ctas
    {
        get => _ctas;
        init => _ctas = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }
    public FooterConfigDto Footer { get; init; } = new();
}

public sealed record PublicExperienceHomeDto
{
    private IReadOnlyList<PublicExperienceHomeBlockDto> _blocks =
        Array.AsReadOnly(Array.Empty<PublicExperienceHomeBlockDto>());

    public string PreferredHomePage { get; init; } = "EventList";
    public string BrandDisplayName { get; init; } = string.Empty;
    public string BrandLogoUrl { get; init; } = string.Empty;
    public string BrandFaviconUrl { get; init; } = string.Empty;
    public IReadOnlyList<PublicExperienceHomeBlockDto> Blocks
    {
        get => _blocks;
        init => _blocks = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }
}

public sealed record PublicExperienceHomeBlockDto
{
    public string Key { get; init; } = string.Empty;
    public PublicExperienceHomeBlockKind Kind { get; init; } = PublicExperienceHomeBlockKind.RichText;
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string ImageUrl { get; init; } = string.Empty;
    public string LinkText { get; init; } = string.Empty;
    public string LinkUrl { get; init; } = string.Empty;
    public int SortOrder { get; init; }
}

public sealed record PublicExperienceNavigationDto
{
    private IReadOnlyList<PublicExperienceNavigationLinkDto> _links =
        Array.AsReadOnly(Array.Empty<PublicExperienceNavigationLinkDto>());

    public IReadOnlyList<PublicExperienceNavigationLinkDto> Links
    {
        get => _links;
        init => _links = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }
}

public sealed record PublicExperienceNavigationLinkDto
{
    public string Label { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public int SortOrder { get; init; }
}

public sealed record PublicExperienceEventCatalogDto
{
    public string Label { get; init; } = "Events";
    public string Url { get; init; } = "/events";
}

public sealed record PublicExperiencePrimaryOrganizationDto
{
    public PublicExperiencePrimaryOrganizationState State { get; init; } = PublicExperiencePrimaryOrganizationState.NotConfigured;
    public Guid? OrganizationId { get; init; }
    public Guid? ActorId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Handle { get; init; } = string.Empty;
    public string WebsiteUrl { get; init; } = string.Empty;
    public string ProfilePictureUri { get; init; } = string.Empty;
}

public sealed record PublicExperienceEventSectionDto
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Url { get; init; } = "/events";
    public string Icon { get; init; } = string.Empty;
    public int SortOrder { get; init; }
}

public sealed record PublicExperienceCtaDto
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public PublicExperienceCtaPlacement Placement { get; init; }
    public PublicExperienceCtaStyle Style { get; init; } = PublicExperienceCtaStyle.Primary;
    public int SortOrder { get; init; }
}
