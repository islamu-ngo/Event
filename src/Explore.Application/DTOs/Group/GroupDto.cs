using System;

namespace Explore.Application.DTOs.Group;

public sealed record GroupDto
{
    public Guid Id { get; init; }
    public Guid ConcurrencyStamp { get; init; }
    public required string FullName { get; init; }
    public string? Description { get; init; }

    // Approval Status
    public int ApprovalStatusId { get; init; }
    public string? ApprovalStatusFullName { get; init; }
    public string? ApprovalStatusMasterCode { get; init; }

    // Tenant
    public Guid TenantId { get; init; }
    public string? TenantFullName { get; init; }

    // Actor
    public Guid? ActorId { get; init; }
    public string? ActorDisplayName { get; init; }
    public string? ActorHandle { get; init; }

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
