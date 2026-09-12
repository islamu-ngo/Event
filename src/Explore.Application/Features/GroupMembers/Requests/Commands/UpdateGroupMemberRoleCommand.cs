using Explore.Application.DTOs.GroupMember;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.GroupMembers.Requests.Commands;

public sealed record UpdateGroupMemberRoleCommand : ICommand<BaseCommandResponse<Guid>>
{
    public required UpdateGroupMemberRoleDto UpdateGroupMemberRoleDto { get; init; }
    public string? RequesterUserId { get; init; }
}
