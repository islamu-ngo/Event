using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using Explore.Domain;

namespace Explore.Application.DTOs.Organization;

public sealed record OrganizationListDto
{
    public Guid Id { get; init; }
    public Guid ConcurrencyStamp { get; init; }
    public Guid TenantId { get; init; }
    public required string FullName { get; init; }
    public string? WebsiteUrl { get; init; }
    public required string Email { get; init; }
    public required string Country { get; init; }
    public required string City { get; init; }
    public required string Postcode { get; init; }
    public required string Address { get; init; }

    public int ApprovalStatusId { get; init; }
    public required string ApprovalStatusFullName { get; init; }
    public string StatusTypeFullName => ApprovalStatusFullName; // Alias for backward compatibility
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
