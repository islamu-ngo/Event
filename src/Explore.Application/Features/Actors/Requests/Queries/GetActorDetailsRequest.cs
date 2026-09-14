using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Actor;

namespace Explore.Application.Features.Actors.Requests.Queries;

public sealed record GetActorDetailsRequest : IQuery<ActorDto?>
{
    public Guid Id { get; init; }
    public Guid? TenantId { get; init; }
}
