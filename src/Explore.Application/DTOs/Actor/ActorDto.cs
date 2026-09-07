using System;
using System.Text.Json.Serialization;

namespace Explore.Application.DTOs.Actor;

/// <summary>
/// Full Actor details with navigation properties
/// Used for GET /api/actor/{id}
/// </summary>
public sealed record ActorDto
{
    public Guid Id { get; init; }
    public Guid ConcurrencyStamp { get; init; }

    // ActorType relationship with i18n support
    public int ActorTypeId { get; init; }
    public string? ActorTypeMasterCode { get; init; } // For i18n with Tolgee
    public string? ActorTypeFullName { get; init; } // Fallback default

    [JsonIgnore]
    public Guid TenantId { get; set; }

    /// <summary>
    /// The User ID this Actor belongs to (if User actor).
    /// </summary>
    [JsonIgnore]
    public Guid? UserId { get; init; }

    /// <summary>
    /// The Organization ID this Actor belongs to (if Organization actor).
    /// </summary>
    public Guid? OrganizationId { get; init; }

    public Guid? GroupId { get; init; }

    [JsonIgnore]
    public bool IsLocallyDiscoverable { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    // ProfilePicture relationship (optional)
    [JsonIgnore]
    public Guid? ProfilePictureId { get; init; }
    public string? ProfilePictureCid { get; init; }
    public string? ProfilePictureUri { get; set; }

    // Federation identifiers (ATProto/ActivityPub)
    public string? Did { get; init; } // Decentralized identifier (e.g., did:plc:xxx)
    public string? Handle { get; init; } // Human-readable handle (e.g., user.bsky.social)

    // DidCustodyType relationship with i18n support (optional)
    public int? DidCustodyTypeId { get; init; }
    public string? DidCustodyTypeMasterCode { get; init; } // For i18n with Tolgee
    public string? DidCustodyTypeFullName { get; init; } // Fallback default

    // Appearance
    public string? BackgroundColor { get; set; }
    public string? BackgroundEffect { get; set; }
    public string? BannerColor { get; set; }
    [JsonIgnore]
    public Guid? BannerPictureId { get; init; }
    public string? BannerPictureUri { get; set; }
    [JsonIgnore]
    public Guid? BackgroundImageId { get; init; }
    public string? BackgroundImageUri { get; set; }

    // Federation metadata
    public string? PdsHost { get; init; } // Personal Data Server host
    public string? Description { get; set; }
    public DateTime? IndexedAt { get; init; }
}
