using System;

namespace Explore.Application.DTOs.User;

public sealed record UserDto
{
    public Guid Id { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    // Actor
    public Guid ActorId { get; init; }
    public string? ActorDisplayName { get; init; }
    public string? ActorHandle { get; init; }

    // Actor Appearance
    public string? ActorBackgroundColor { get; init; }
    public string? ActorBackgroundEffect { get; init; }
    public string? ActorBannerColor { get; init; }
    public Guid? ActorBannerPictureId { get; init; }
    public string? ActorBannerPictureUri { get; init; }
    public Guid? ActorBackgroundImageId { get; init; }
    public string? ActorBackgroundImageUri { get; init; }

    // Auth
    public string? AuthProvider { get; init; }
    public string? AuthProviderId { get; init; }
    public bool? EmailVerified { get; init; }
    public Guid ConcurrencyStamp { get; init; }

    // Profile image key (S3 object key) and URI for preview
    public string? ProfileImageKey { get; init; }
    public string? ProfileImageUri { get; set; }
}
