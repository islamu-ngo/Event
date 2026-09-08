using System;

namespace Explore.Application.DTOs.Group;

public sealed record GroupListDto
{
    public Guid Id { get; init; }
    public Guid ConcurrencyStamp { get; init; }
    public Guid TenantId { get; init; }
    public required string FullName { get; init; }
    public string? Description { get; init; }

    public int ApprovalStatusId { get; init; }
    public required string ApprovalStatusFullName { get; init; }
    public DateTime CreatedAt { get; init; }
    public int? CurrentUserRoleId { get; set; }

    // Profile Picture (resolved to presigned URL)
    public Guid? ActorProfilePictureId { get; init; }
    public string? ActorProfilePictureUri { get; set; }
    public string? ActorBackgroundColor { get; init; }
    public string? ActorBackgroundEffect { get; init; }
    public string? ActorBannerColor { get; init; }
    public Guid? ActorBannerPictureId { get; init; }
    public string? ActorBannerPictureUri { get; init; }
    public Guid? ActorBackgroundImageId { get; init; }
    public string? ActorBackgroundImageUri { get; init; }
}
