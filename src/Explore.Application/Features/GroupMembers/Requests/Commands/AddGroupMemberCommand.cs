using Explore.Application.DTOs.GroupMember;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.GroupMembers.Requests.Commands;

public sealed record AddGroupMemberCommand : IRequest<BaseCommandResponse<Guid>>
{
    public required AddGroupMemberDto AddGroupMemberDto { get; init; }
    public string? RequesterUserId { get; init; }
}
