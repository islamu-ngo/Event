using System;

namespace Explore.Application.DTOs.Actor;

/// <summary>
/// Create Actor payload (no Id)
/// Used for POST /api/actor
/// An Actor must be linked to either a User OR an Organization (exactly one, not both, not neither)
/// </summary>
public sealed record CreateActorDto
{
    public int ActorTypeId { get; init; }

    public Guid TenantId { get; init; }

    /// <summary>
    /// The User ID this Actor belongs to. Required if this is a User actor.
    /// Must be null if OrganizationId is set.
    /// </summary>
    public Guid? UserId { get; init; }

    /// <summary>
    /// The Organization ID this Actor belongs to. Required if this is an Organization actor.
    /// Must be null if UserId is set.
    /// </summary>
    public Guid? OrganizationId { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public Guid? ProfilePictureId { get; init; }

    // Appearance
    public string? BackgroundColor { get; init; }
    public string? BackgroundEffect { get; init; }
    public string? BannerColor { get; init; }
    public Guid? BannerPictureId { get; init; }

    // Federation identifiers (optional on creation)
    public string? Did { get; init; }
    public string? Handle { get; init; }

    public int? DidCustodyTypeId { get; init; }

    // Federation metadata
    public string? PdsHost { get; init; }
    public string? Description { get; init; }
    public DateTime? IndexedAt { get; init; }

    // Content addressing
    public string? ProfilePictureCid { get; init; }
    public string? ExternalProfilePictureUri { get; init; }
}
