using System;
using Explore.Application.DTOs.Organization;
using Explore.Domain.Enums;

namespace Explore.Application.DTOs.OrganizationMember;

public sealed record OrganizationInvitationDto
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public required string? OrganizationName { get; init; }
    public RoleEnum Role { get; init; }
    public required string? Email { get; init; }
}
