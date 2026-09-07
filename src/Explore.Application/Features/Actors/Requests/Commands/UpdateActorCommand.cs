using System;
using Explore.Application.Authorization;
using Explore.Application.DTOs.Actor;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Actors.Requests.Commands;

[AuthorizeResource(ResourceKinds.Actor, AuthorizationActions.Update)]
public sealed record UpdateActorCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid ActorId { get; init; }

    public Guid ExpectedConcurrencyStamp { get; init; }

    public required UpdateActorDto UpdateActorDto { get; init; }

    string? ISecureRequest.ResourceId => ActorId.ToString();
}
