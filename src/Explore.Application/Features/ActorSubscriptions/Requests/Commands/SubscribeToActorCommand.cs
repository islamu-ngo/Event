using Explore.Application.Authorization;
using Explore.Application.DTOs.ActorSubscription;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.ActorSubscriptions.Requests.Commands;

[AuthorizeResource(ResourceKinds.ActorSubscription, AuthorizationActions.ActorSubscriptions.Create)]
public sealed record SubscribeToActorCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required SubscribeToActorDto Subscription { get; init; }

    public string? ResourceId => Subscription.TargetActorId == Guid.Empty ? null : Subscription.TargetActorId.ToString();

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        Subscription.TargetActorId == Guid.Empty
        ? null
        : new PersonalResourceAuthorizationFacts(Guid.Empty, null);
}
