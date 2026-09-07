using Explore.Application.Authorization;
using Explore.Application.DTOs.ActorSubscription;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.ActorSubscriptions.Requests.Commands;

[AuthorizeResource(ResourceKinds.ActorSubscription, AuthorizationActions.ActorSubscriptions.Create)]
public sealed record SubscribeToActorCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required SubscribeToActorDto Subscription { get; init; }

    public string? ResourceId => Subscription.TargetActorId == Guid.Empty ? null : Subscription.TargetActorId.ToString();

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        Subscription.TargetActorId == Guid.Empty
        ? null
        : new PersonalResourceAuthorizationFacts(Guid.Empty, null);
}
