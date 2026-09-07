namespace Explore.API.Hateoas.Policies;

using System.Collections.Generic;
using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.ActorSubscription;
using Explore.Application.Hateoas;

public sealed class ActorSubscriptionDetailLinkPolicy : ILinkPolicy<ActorSubscriptionDto>
{
    public IEnumerable<LinkDefinition> GetLinks(ActorSubscriptionDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetActorSubscriptionByActor,
            new { targetActorId = dto.TargetActorId },
            "GET",
            dto.TargetActorName)
            .RequirePermission(AuthorizationActions.ActorSubscriptions.View, ResourceDescriptors.ActorSubscription, dto);

        yield return new LinkDefinition(
            LinkRelations.Collection,
            RouteNames.GetActorSubscriptions,
            null,
            "GET",
            "My actor subscriptions")
            .RequirePermission(AuthorizationActions.ActorSubscriptions.View, ResourceKinds.ActorSubscription);

        yield return new LinkDefinition(
            LinkRelations.Actor,
            RouteNames.GetActorById,
            new { id = dto.TargetActorId },
            "GET",
            dto.TargetActorName);

        yield return new LinkDefinition(
            "update-notification-level",
            RouteNames.UpdateActorSubscriptionNotificationLevel,
            new { targetActorId = dto.TargetActorId },
            "PATCH",
            "Update notification level",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.ActorSubscriptions.Update, ResourceDescriptors.ActorSubscription, dto);

        yield return new LinkDefinition(
            "unsubscribe",
            RouteNames.UnsubscribeFromActor,
            new { targetActorId = dto.TargetActorId },
            "DELETE",
            "Unsubscribe from actor",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.ActorSubscriptions.Delete, ResourceDescriptors.ActorSubscription, dto);
    }
}

public sealed class ActorSubscriptionCollectionLinkPolicy : ICollectionLinkPolicy<ActorSubscriptionListDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(ActorSubscriptionListDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetActorSubscriptionByActor,
            new { targetActorId = dto.TargetActorId },
            "GET",
            dto.TargetActorName)
            .RequirePermission(AuthorizationActions.ActorSubscriptions.View, ResourceDescriptors.ActorSubscriptionList, dto);

        yield return new LinkDefinition(
            LinkRelations.Actor,
            RouteNames.GetActorById,
            new { id = dto.TargetActorId },
            "GET",
            dto.TargetActorName);

        yield return new LinkDefinition(
            "update-notification-level",
            RouteNames.UpdateActorSubscriptionNotificationLevel,
            new { targetActorId = dto.TargetActorId },
            "PATCH",
            "Update notification level",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.ActorSubscriptions.Update, ResourceDescriptors.ActorSubscriptionList, dto);

        yield return new LinkDefinition(
            "unsubscribe",
            RouteNames.UnsubscribeFromActor,
            new { targetActorId = dto.TargetActorId },
            "DELETE",
            "Unsubscribe from actor",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.ActorSubscriptions.Delete, ResourceDescriptors.ActorSubscriptionList, dto);
    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Create,
            RouteNames.SubscribeToActor,
            null,
            "POST",
            "Subscribe to actor",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.ActorSubscriptions.Create, ResourceKinds.ActorSubscription);
    }
}
