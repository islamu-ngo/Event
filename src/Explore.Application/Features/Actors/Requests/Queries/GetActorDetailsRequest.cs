using Explore.Application.DTOs.Actor;
using MediatR;

namespace Explore.Application.Features.Actors.Requests.Queries;

public sealed record GetActorDetailsRequest : IRequest<ActorDto?>
{
    public Guid Id { get; init; }
    public Guid? TenantId { get; init; }
}
