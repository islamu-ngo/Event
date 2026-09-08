using System;

namespace Explore.Application.DTOs.OrganizationMember;

public sealed record OrganizationMemberDto
{
    public Guid Id { get; init; }

    public Guid TenantId { get; init; }

    // Organization
    public Guid OrganizationId { get; init; }
    public string? OrganizationFullName { get; init; }

    // User
    public Guid UserId { get; init; }
    public string? UserEmail { get; init; }
    public string? UserFullName { get; init; }

    // Role
    public int RoleId { get; init; }
    public string? RoleName { get; init; }

    // Position
    public int? OrganizationPositionId { get; init; }
    public string? OrganizationPositionFullName { get; init; }
}
