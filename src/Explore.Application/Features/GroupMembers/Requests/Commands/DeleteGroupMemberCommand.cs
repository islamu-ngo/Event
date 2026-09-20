using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.GroupMembers.Requests.Commands;

public sealed record DeleteGroupMemberCommand : ICommand<BaseCommandResponse<Guid>>
{
    public Guid MemberId { get; init; }
    public string? RequesterUserId { get; init; }
}
