namespace Explore.Application.DTOs.TenantSettingsDocuments;

using Explore.Application.Models.Common;

public sealed record TenantBrandingSettingsDocumentDto
{
    public required string DocumentKey { get; init; }

    public required int SchemaVersion { get; init; }

    public required string DefaultsVersion { get; init; }

    public required TenantBrandingSettingsPayloadDto Payload { get; init; }

    public required string Source { get; init; }

    public required Guid SourceScopeId { get; init; }

    public required Guid ConcurrencyStamp { get; init; }

    public bool IsLockedByInstance { get; init; }

    public bool CanChangeDisplayName { get; init; }

    public bool CanChangeLogoUrl { get; init; }

    public bool CanChangeFaviconUrl { get; init; }

    public bool CanChangeCustomCssUrl { get; init; }

    public DateTime? UpdatedAt { get; init; }
}

public sealed record PatchTenantBrandingSettingsDocumentDto
{
    public required Guid ExpectedConcurrencyStamp { get; init; }

    public PatchTenantBrandingDisplayNameDto? DisplayName { get; init; }

    public PatchTenantBrandingAssetsDto? Assets { get; init; }
}

public sealed record PatchTenantBrandingDisplayNameDto
{
    public OptionalUpdate<string?> Value { get; init; } = OptionalUpdate<string?>.Unspecified();
}

public sealed record PatchTenantBrandingAssetsDto
{
    public OptionalUpdate<string?> LogoUrl { get; init; } = OptionalUpdate<string?>.Unspecified();

    public OptionalUpdate<string?> FaviconUrl { get; init; } = OptionalUpdate<string?>.Unspecified();

    public OptionalUpdate<string?> CustomCssUrl { get; init; } = OptionalUpdate<string?>.Unspecified();
}

public sealed record TenantBrandingSettingsPayloadDto
{
    public string? DisplayName { get; init; }

    public string? LogoUrl { get; init; }

    public string? FaviconUrl { get; init; }

    public string? CustomCssUrl { get; init; }
}
