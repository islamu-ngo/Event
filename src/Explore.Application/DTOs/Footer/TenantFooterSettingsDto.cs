namespace Explore.Application.DTOs.Footer;

using Explore.Application.Models.Common;

public sealed record TenantFooterSettingsDto
{
    public required Guid TenantId { get; init; }

    public bool Enabled { get; init; }

    public string Template { get; init; } = "standard-3-col";

    public bool ShowDescription { get; init; }

    public string DescriptionText { get; init; } = string.Empty;

    public bool ShowSocialLinks { get; init; }

    public IReadOnlyList<FooterSocialLinkDto> SocialLinks { get; init; } = [];

    public string CopyrightText { get; init; } = string.Empty;

    public bool ShowCookieSettingsLink { get; init; }

    public bool LockTenantTemplate { get; init; }

    public bool LockTenantDescription { get; init; }

    public bool LockTenantLinkGroups { get; init; }

    public bool LockTenantSocialLinks { get; init; }

    public bool LockTenantCopyright { get; init; }
}

public sealed record PatchTenantFooterSettingsDto
{
    public PatchTenantFooterGeneralDto? General { get; init; }

    public PatchTenantFooterTemplateDto? Template { get; init; }

    public PatchTenantFooterDescriptionDto? Description { get; init; }

    public PatchTenantFooterSocialLinksDto? SocialLinks { get; init; }

    public PatchTenantFooterCopyrightDto? Copyright { get; init; }
}

public sealed record PatchTenantFooterGeneralDto
{
    public OptionalUpdate<bool> Enabled { get; init; } = OptionalUpdate<bool>.Unspecified();

    public OptionalUpdate<bool> ShowCookieSettingsLink { get; init; } = OptionalUpdate<bool>.Unspecified();
}

public sealed record PatchTenantFooterTemplateDto
{
    public OptionalUpdate<string> Value { get; init; } = OptionalUpdate<string>.Unspecified();
}

public sealed record PatchTenantFooterDescriptionDto
{
    public OptionalUpdate<bool> Show { get; init; } = OptionalUpdate<bool>.Unspecified();

    public OptionalUpdate<string> Text { get; init; } = OptionalUpdate<string>.Unspecified();
}

public sealed record PatchTenantFooterSocialLinksDto
{
    public OptionalUpdate<bool> Show { get; init; } = OptionalUpdate<bool>.Unspecified();

    public OptionalUpdate<IReadOnlyList<FooterSocialLinkDto>> Items { get; init; }
        = OptionalUpdate<IReadOnlyList<FooterSocialLinkDto>>.Unspecified();
}

public sealed record PatchTenantFooterCopyrightDto
{
    public OptionalUpdate<string> Text { get; init; } = OptionalUpdate<string>.Unspecified();
}
