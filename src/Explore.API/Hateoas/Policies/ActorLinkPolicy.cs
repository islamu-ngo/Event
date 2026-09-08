namespace Explore.API.Hateoas.Policies;

using System.Security.Claims;
using Explore.API.Hateoas;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Actor;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;

/// <summary>
/// Link policy for ActorDto (detail view).
/// </summary>
public sealed class ActorDetailLinkPolicy : ILinkPolicy<ActorDto>
{
    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetLinks(ActorDto dto, ClaimsPrincipal? user)
    {
        // Self link
        yield return dto.TenantId == Guid.Empty
            ? new LinkDefinition(
                LinkRelations.Self,
                RouteNames.GetActorById,
                new { id = dto.Id },
                "GET",
                dto.DisplayName)
            : new LinkDefinition(
                LinkRelations.Self,
                RouteNames.GetActorByTenant,
                new { tenantId = dto.TenantId, id = dto.Id },
                "GET",
                dto.DisplayName);

        // Collection link
        yield return new LinkDefinition(
            LinkRelations.Collection,
            RouteNames.GetActors,
            null,
            "GET",
            "All actors");

        // Organization link (if organization actor)
        if (dto.OrganizationId.HasValue)
        {
            yield return new LinkDefinition(
                LinkRelations.Organization,
                RouteNames.GetOrganizationById,
                new { id = dto.OrganizationId },
                "GET",
                "Organization");
        }

        if (dto.GroupId.HasValue)
        {
            yield return new LinkDefinition(
                LinkRelations.Group,
                RouteNames.GetGroupById,
                new { id = dto.GroupId },
                "GET",
                "Group");
        }

        if (dto.IsLocallyDiscoverable && CanSubscribe(dto.ActorTypeId))
        {
            yield return new LinkDefinition(
                "subscription",
                RouteNames.GetActorSubscriptionByActor,
                new { targetActorId = dto.Id },
                "GET",
                "My subscription to this actor",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.ActorSubscriptions.View,
                    ResourceKinds.ActorSubscription,
                    dto.Id.ToString(),
                    facts: SubscriptionFacts(dto.TenantId));

            yield return new LinkDefinition(
                "subscribe",
                RouteNames.SubscribeToActor,
                null,
                "POST",
                "Subscribe to this actor",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.ActorSubscriptions.Create,
                    ResourceKinds.ActorSubscription,
                    dto.Id.ToString(),
                    facts: SubscriptionFacts(dto.TenantId));
        }
    }

    private static bool CanSubscribe(int actorTypeId) => actorTypeId is (int)ActorTypeEnum.Organization or (int)ActorTypeEnum.Group;

    // The subscription belongs to the caller, not to the actor being followed; the handler enforces
    // owner identity, so the tenant is the only policy fact.
    private static IAuthorizationFacts SubscriptionFacts(Guid tenantId) =>
        new PersonalResourceAuthorizationFacts(tenantId);
}

/// <summary>
/// Link policy for ActorListDto (collection items).
/// </summary>
public sealed class ActorCollectionLinkPolicy : ICollectionLinkPolicy<ActorListDto>
{
    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetItemLinks(ActorListDto dto, ClaimsPrincipal? user)
    {
        // Self link for item
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetActorById,
            new { id = dto.Id },
            "GET",
            dto.DisplayName);

        if (dto.IsLocallyDiscoverable && CanSubscribe(dto.ActorTypeId))
        {
            yield return new LinkDefinition(
                "subscription",
                RouteNames.GetActorSubscriptionByActor,
                new { targetActorId = dto.Id },
                "GET",
                "My subscription to this actor",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.ActorSubscriptions.View,
                    ResourceKinds.ActorSubscription,
                    dto.Id.ToString(),
                    facts: SubscriptionFacts(dto.TenantId));

            yield return new LinkDefinition(
                "subscribe",
                RouteNames.SubscribeToActor,
                null,
                "POST",
                "Subscribe to this actor",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.ActorSubscriptions.Create,
                    ResourceKinds.ActorSubscription,
                    dto.Id.ToString(),
                    facts: SubscriptionFacts(dto.TenantId));
        }
    }

    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        // Actors are typically read-only (created via user/organization registration)
        yield break;
    }

    private static bool CanSubscribe(int actorTypeId) => actorTypeId is (int)ActorTypeEnum.Organization or (int)ActorTypeEnum.Group;

    // The subscription belongs to the caller, not to the actor being followed; the handler enforces
    // owner identity, so the tenant is the only policy fact.
    private static IAuthorizationFacts SubscriptionFacts(Guid tenantId) =>
        new PersonalResourceAuthorizationFacts(tenantId);
}
