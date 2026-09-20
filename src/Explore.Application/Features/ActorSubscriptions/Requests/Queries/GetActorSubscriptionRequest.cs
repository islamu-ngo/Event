using Explore.Application.Authorization;
using Explore.Application.DTOs.ActorSubscription;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.ActorSubscriptions.Requests.Queries;

[AuthorizeResource(ResourceKinds.ActorSubscription, AuthorizationActions.ActorSubscriptions.View)]
public sealed record GetActorSubscriptionRequest : IQuery<ActorSubscriptionDto?>, ISecureRequest
{
    public Guid TargetActorId { get; init; }

    public string? ResourceId => TargetActorId == Guid.Empty ? null : TargetActorId.ToString();

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        TargetActorId == Guid.Empty
        ? null
        : new PersonalResourceAuthorizationFacts(Guid.Empty, null);
}
