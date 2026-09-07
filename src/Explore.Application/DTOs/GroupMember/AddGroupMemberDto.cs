using System;
using Explore.Domain.Enums;

namespace Explore.Application.DTOs.GroupMember;

public sealed record AddGroupMemberDto
{
    public Guid GroupId { get; init; }
    public required string Email { get; init; }
    public RoleEnum Role { get; init; } = RoleEnum.GroupMember;
    public int? GroupPositionId { get; init; }
}
