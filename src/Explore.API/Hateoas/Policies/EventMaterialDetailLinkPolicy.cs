using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.EventResource;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;

namespace Explore.API.Hateoas.Policies;

public sealed class EventMaterialDetailLinkPolicy(ITenantContext tenantContext)
    : ILinkPolicy<EventResourceManagementDto>
{
    public IEnumerable<LinkDefinition> GetLinks(EventResourceManagementDto dto, ClaimsPrincipal? user)
    {
        if (tenantContext.TenantId == Guid.Empty || dto.Id == Guid.Empty || dto.EventId == Guid.Empty)
            yield break;
        yield return Resource(LinkDefinition.Self(RouteNames.GetEventResourceManagementDetail, new { id = dto.Id })
            .Authenticated(), dto, AuthorizationActions.EventResources.ViewManagement);
        yield return LinkDefinition.Collection(RouteNames.ListEventResourceManagement, new { eventId = dto.EventId })
            .Authenticated().RequirePermission(AuthorizationActions.EventResources.ViewManagement,
                ResourceKinds.EventResource, dto.EventId.ToString("D"),
                new AuthorizationScope(TenantId: tenantContext.TenantId.ToString("D")),
                new EventResourceCollectionAuthorizationFacts(tenantContext.TenantId, dto.EventId));
        yield return Resource(LinkDefinition.Action(LinkRelations.ViewAudit, RouteNames.GetEventResourceAudit,
            HttpMethods.Get, new { id = dto.Id }), dto, AuthorizationActions.EventResources.ViewAudit);
        yield return Resource(LinkDefinition.Delete(RouteNames.DeleteEventResource, new { id = dto.Id }),
            dto, AuthorizationActions.EventResources.Delete);
        if (dto.PublicationState != EventResourcePublicationStateEnum.Archived)
            yield return Resource(LinkDefinition.Edit(RouteNames.UpdateEventResource, new { id = dto.Id }),
                dto, AuthorizationActions.EventResources.Update);
        if (dto.PublicationState is EventResourcePublicationStateEnum.Draft or EventResourcePublicationStateEnum.Withdrawn)
            yield return Resource(LinkDefinition.Action(LinkRelations.Archive, RouteNames.ArchiveEventResource,
                HttpMethods.Post, new { id = dto.Id }), dto, AuthorizationActions.EventResources.Archive);
        if (dto.PublicationState == EventResourcePublicationStateEnum.Published)
        {
            yield return Resource(LinkDefinition.Action(LinkRelations.Unpublish, RouteNames.UnpublishEventResource,
                HttpMethods.Post, new { id = dto.Id }), dto, AuthorizationActions.EventResources.Unpublish);
            yield return Resource(LinkDefinition.Action(LinkRelations.Moderate, RouteNames.ModerateEventResource,
                HttpMethods.Post, new { id = dto.Id }), dto, AuthorizationActions.EventResources.Moderate);
        }
    }

    private LinkDefinition Resource(LinkDefinition link, EventResourceManagementDto dto, string action) =>
        link.RequirePermission(action, ResourceKinds.EventResource, dto.Id.ToString("D"),
            new AuthorizationScope(TenantId: tenantContext.TenantId.ToString("D")),
            new EventResourceTargetAuthorizationFacts(tenantContext.TenantId, dto.Id));
}
