using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.EventResource;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class EventMaterialAudienceDetailLinkPolicy(ITenantContext tenant)
    : ILinkPolicy<EventResourceAudienceDetailDto>
{
    public IEnumerable<LinkDefinition> GetLinks(EventResourceAudienceDetailDto dto, ClaimsPrincipal? user)
    {
        if (tenant.TenantId == Guid.Empty || dto.Id == Guid.Empty || dto.EventId == Guid.Empty) yield break;
        yield return LinkDefinition.Self(RouteNames.GetEventResourceAudienceDetail, new { id = dto.Id });
        yield return LinkDefinition.Collection(RouteNames.ListEventResources, new { eventId = dto.EventId });
        if (!dto.IsTeaser && dto.File is not null && dto.Availability == "available")
            yield return LinkDefinition.Action("download", RouteNames.GetEventResourceContent,
                    HttpMethods.Get, new { id = dto.Id }, requiresAuth: false)
                .RequirePermission(AuthorizationActions.EventResources.Download, ResourceKinds.EventResource,
                    dto.Id.ToString("D"), new AuthorizationScope(TenantId: tenant.TenantId.ToString("D")),
                    new EventResourceTargetAuthorizationFacts(tenant.TenantId, dto.Id));
        if (!dto.IsTeaser && dto.ExternalDestinationSafeOrigin is not null && dto.Availability == "available")
            yield return LinkDefinition.Action("access", RouteNames.GetEventResourceAccess,
                    HttpMethods.Get, new { id = dto.Id }, requiresAuth: false)
                .RequirePermission(AuthorizationActions.EventResources.Access, ResourceKinds.EventResource,
                    dto.Id.ToString("D"), new AuthorizationScope(TenantId: tenant.TenantId.ToString("D")),
                    new EventResourceTargetAuthorizationFacts(tenant.TenantId, dto.Id));
        yield return LinkDefinition.Action(LinkRelations.Management, RouteNames.GetEventResourceManagementDetail,
                HttpMethods.Get, new { id = dto.Id }).Authenticated()
            .RequirePermission(AuthorizationActions.EventResources.ViewManagement, ResourceKinds.EventResource,
                dto.Id.ToString("D"), new AuthorizationScope(TenantId: tenant.TenantId.ToString("D")),
                new EventResourceTargetAuthorizationFacts(tenant.TenantId, dto.Id));
        if (dto.AccessibleAlternativeEventResourceId is { } alternative)
            yield return LinkDefinition.Action(LinkRelations.AccessibleAlternative, RouteNames.GetEventResourceAudienceDetail,
                HttpMethods.Get, new { id = alternative }, requiresAuth: false);
    }
}
