using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.EventResource;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class EventMaterialAudienceCollectionLinkPolicy(ITenantContext tenant)
    : ICollectionLinkPolicy<EventResourceAudienceDetailDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(EventResourceAudienceDetailDto dto, ClaimsPrincipal? user)
    {
        if (tenant.TenantId == Guid.Empty || dto.Id == Guid.Empty) yield break;
        yield return LinkDefinition.Self(RouteNames.GetEventResourceAudienceDetail, new { id = dto.Id });
        if (dto.AccessibleAlternativeEventResourceId is { } alternative)
            yield return LinkDefinition.Action(LinkRelations.AccessibleAlternative, RouteNames.GetEventResourceAudienceDetail,
                HttpMethods.Get, new { id = alternative }, requiresAuth: false);
    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user, ICollectionAuthorizationContext? authorizationContext)
    {
        if (authorizationContext is not EventMaterialAudienceCollectionContext context
            || context.TenantId == Guid.Empty || context.TenantId != tenant.TenantId || context.EventId == Guid.Empty)
            yield break;
        yield return LinkDefinition.Self(RouteNames.ListEventResources,
            new { eventId = context.EventId, pageSize = context.PageSize, cursor = context.Cursor });
        if (context.NextCursor is { } next)
            yield return LinkDefinition.Action(LinkRelations.Next, RouteNames.ListEventResources, HttpMethods.Get,
                new { eventId = context.EventId, pageSize = context.PageSize, cursor = next }, requiresAuth: false);
        yield return LinkDefinition.Action(LinkRelations.ManageResources, RouteNames.ListEventResourceManagement,
                HttpMethods.Get, new { eventId = context.EventId }).Authenticated()
            .RequirePermission(AuthorizationActions.EventResources.ViewManagement, ResourceKinds.EventResource,
                context.EventId.ToString("D"), new AuthorizationScope(TenantId: context.TenantId.ToString("D")),
                new EventResourceCollectionAuthorizationFacts(context.TenantId, context.EventId));
        yield return LinkDefinition.Action(LinkRelations.CreateResource, RouteNames.CreateEventResource,
                HttpMethods.Post, new { eventId = context.EventId }).Authenticated()
            .RequirePermission(AuthorizationActions.EventResources.Create, ResourceKinds.EventResource,
                context.EventId.ToString("D"), new AuthorizationScope(TenantId: context.TenantId.ToString("D")),
                new EventResourceTargetAuthorizationFacts(context.TenantId, context.EventId));
    }
}

public sealed record EventMaterialAudienceCollectionContext(
    Guid TenantId, Guid EventId, int PageSize, string? Cursor, string? NextCursor) : ICollectionAuthorizationContext
{
    public string AuthorizationResourceId => EventId.ToString("D");
    public IAuthorizationFacts AuthorizationFacts => new EventResourceCollectionAuthorizationFacts(TenantId, EventId);
}
