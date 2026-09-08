using Explore.Application.DTOs.Group;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Groups.Requests.Commands;

public sealed record CreateGroupCommand : IRequest<BaseCommandResponse<Guid>>
{
    public required CreateGroupDto GroupDto { get; init; }
    public required Guid CreatorUserId { get; init; }
}
