using Explore.Application.DTOs.GroupMember;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.GroupMembers.Requests.Commands;

public sealed record UpdateGroupMemberRoleCommand : IRequest<BaseCommandResponse<Guid>>
{
    public required UpdateGroupMemberRoleDto UpdateGroupMemberRoleDto { get; init; }
    public string? RequesterUserId { get; init; }
}
