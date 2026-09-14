using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Actor;

namespace Explore.Application.Features.Actors.Requests.Queries;

public sealed record GetActorByDidRequest : IQuery<ActorDto?>
{
    public required string Did { get; init; }
}
