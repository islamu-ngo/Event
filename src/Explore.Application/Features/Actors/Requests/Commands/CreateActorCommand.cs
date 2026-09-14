using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Actor;
using Explore.Application.Responses;

namespace Explore.Application.Features.Actors.Requests.Commands;

public sealed record CreateActorCommand : ICommand<BaseCommandResponse<Guid>>
{
    public required CreateActorDto ActorDto { get; init; }
}
