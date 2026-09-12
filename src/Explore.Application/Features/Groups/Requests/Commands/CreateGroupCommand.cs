using Explore.Application.DTOs.Group;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Groups.Requests.Commands;

public sealed record CreateGroupCommand : ICommand<BaseCommandResponse<Guid>>
{
    public required CreateGroupDto GroupDto { get; init; }
    public required Guid CreatorUserId { get; init; }
}
