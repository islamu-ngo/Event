using Explore.Application.DTOs.GroupMember;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.GroupMembers.Requests.Commands;

public sealed record AddGroupMemberCommand : ICommand<BaseCommandResponse<Guid>>
{
    public required AddGroupMemberDto AddGroupMemberDto { get; init; }
    public string? RequesterUserId { get; init; }
}
