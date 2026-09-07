using Explore.Application.Authorization;
using Explore.Application.DTOs.ActorSubscription;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.ActorSubscriptions.Requests.Queries;

[AuthorizeResource(ResourceKinds.ActorSubscription, AuthorizationActions.ActorSubscriptions.View)]
public sealed record GetActorSubscriptionsRequest : IRequest<PaginatedResult<ActorSubscriptionListDto>>, ISecureRequest
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;

    public string? ResourceId => "current-user-subscriptions";
}
