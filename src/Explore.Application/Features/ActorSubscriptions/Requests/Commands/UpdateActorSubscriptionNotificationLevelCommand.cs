using Explore.Application.Authorization;
using Explore.Application.DTOs.ActorSubscription;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.ActorSubscriptions.Requests.Commands;

[AuthorizeResource(ResourceKinds.ActorSubscription, AuthorizationActions.ActorSubscriptions.Update)]
public sealed record UpdateActorSubscriptionNotificationLevelCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid TargetActorId { get; init; }
    public required UpdateActorSubscriptionNotificationLevelDto Patch { get; init; }

    public string? ResourceId => TargetActorId == Guid.Empty ? null : TargetActorId.ToString();

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        TargetActorId == Guid.Empty
        ? null
        : new PersonalResourceAuthorizationFacts(Guid.Empty, null);
}
