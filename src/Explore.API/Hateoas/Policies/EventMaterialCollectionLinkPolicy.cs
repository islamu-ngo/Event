using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.EventResource;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;

namespace Explore.API.Hateoas.Policies;

public sealed class EventMaterialCollectionLinkPolicy(ITenantContext tenantContext)
    : ICollectionLinkPolicy<EventResourceManagementDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(EventResourceManagementDto dto, ClaimsPrincipal? user)
    {
        if (tenantContext.TenantId == Guid.Empty || dto.Id == Guid.Empty) yield break;
        yield return Resource(LinkDefinition.Self(RouteNames.GetEventResourceManagementDetail, new { id = dto.Id })
            .Authenticated(), dto, AuthorizationActions.EventResources.ViewManagement);
        if (dto.PublicationState != EventResourcePublicationStateEnum.Archived)
            yield return Resource(LinkDefinition.Edit(RouteNames.UpdateEventResource, new { id = dto.Id }),
                dto, AuthorizationActions.EventResources.Update);
        yield return Resource(LinkDefinition.Delete(RouteNames.DeleteEventResource, new { id = dto.Id }),
            dto, AuthorizationActions.EventResources.Delete);
    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user, ICollectionAuthorizationContext? authorizationContext)
    {
        if (authorizationContext is not EventMaterialCollectionAuthorizationContext context
            || context.TenantId == Guid.Empty || context.TenantId != tenantContext.TenantId || context.EventId == Guid.Empty)
            yield break;
        yield return LinkDefinition.Action(LinkRelations.CreateResource, RouteNames.CreateEventResource,
            HttpMethods.Post, new { eventId = context.EventId })
            .RequirePermission(AuthorizationActions.EventResources.Create, ResourceKinds.EventResource,
                context.EventId.ToString("D"), new AuthorizationScope(TenantId: context.TenantId.ToString("D")),
                new EventResourceTargetAuthorizationFacts(context.TenantId, context.EventId));
        yield return LinkDefinition.Action(LinkRelations.ExportResourceMetadata, RouteNames.ExportEventResourceMetadata,
                HttpMethods.Get, new { eventId = context.EventId })
            .RequirePermission(AuthorizationActions.EventResources.Export, ResourceKinds.EventResource,
                context.EventId.ToString("D"), new AuthorizationScope(TenantId: context.TenantId.ToString("D")),
                new EventResourceCollectionAuthorizationFacts(context.TenantId, context.EventId));
    }

    private LinkDefinition Resource(LinkDefinition link, EventResourceManagementDto dto, string action) =>
        link.RequirePermission(action, ResourceKinds.EventResource, dto.Id.ToString("D"),
            new AuthorizationScope(TenantId: tenantContext.TenantId.ToString("D")),
            new EventResourceTargetAuthorizationFacts(tenantContext.TenantId, dto.Id));
}

public sealed record EventMaterialCollectionAuthorizationContext(Guid TenantId, Guid EventId, int Page = 1, int PageSize = 20)
    : ICollectionAuthorizationContext
{
    public string AuthorizationResourceId => EventId.ToString("D");
    public IAuthorizationFacts AuthorizationFacts => new EventResourceCollectionAuthorizationFacts(TenantId, EventId);
}
