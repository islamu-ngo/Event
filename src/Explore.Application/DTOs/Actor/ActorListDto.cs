using System;
using System.Text.Json.Serialization;

namespace Explore.Application.DTOs.Actor;

/// <summary>
/// Actor list view with minimal properties
/// Used for GET /api/actor
/// </summary>
public sealed record ActorListDto
{
    public Guid Id { get; init; }
    public Guid ConcurrencyStamp { get; init; }

    // ActorType with i18n support
    public int ActorTypeId { get; init; }
    public string? ActorTypeMasterCode { get; init; } // For i18n with Tolgee
    public string? ActorTypeFullName { get; init; } // Fallback default

    [JsonIgnore]
    public Guid TenantId { get; init; }

    [JsonIgnore]
    public bool IsLocallyDiscoverable { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    // Federation identifiers
    public string? Did { get; init; }
    public string? Handle { get; init; }

    // DidCustodyType with i18n support (optional)
    public int? DidCustodyTypeId { get; init; }
    public string? DidCustodyTypeMasterCode { get; init; } // For i18n with Tolgee
    public string? DidCustodyTypeFullName { get; init; } // Fallback default

    // ProfilePicture
    [JsonIgnore]
    public Guid? ProfilePictureId { get; init; }
    public string? ProfilePictureUri { get; set; }

    // Appearance
    public string? BackgroundColor { get; set; }
    public string? BackgroundEffect { get; set; }
    public string? BannerColor { get; set; }
    [JsonIgnore]
    public Guid? BannerPictureId { get; init; }
    public string? BannerPictureUri { get; init; }
    [JsonIgnore]
    public Guid? BackgroundImageId { get; init; }
    public string? BackgroundImageUri { get; init; }

    public string? PdsHost { get; init; }
    public DateTime? IndexedAt { get; init; }
}
