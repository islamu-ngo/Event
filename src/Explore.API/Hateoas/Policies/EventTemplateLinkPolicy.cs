namespace Explore.API.Hateoas.Policies;

using System.Collections.Generic;
using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EventTemplate;
using Explore.Application.Hateoas;

/// <summary>
/// Link policy for EventTemplateDto (detail view).
/// Determines which links should be included based on resource state and user authorization.
/// </summary>
public sealed class EventTemplateDetailLinkPolicy : ILinkPolicy<EventTemplateDto>
{
    public IEnumerable<LinkDefinition> GetLinks(EventTemplateDto dto, ClaimsPrincipal? user)
    {
        // Self link - always included
        yield return LinkDefinition.Self(
            RouteNames.GetEventTemplateById,
            new { id = dto.Id });

        // Collection link
        yield return LinkDefinition.Collection(
            RouteNames.GetEventTemplates,
            new { eventTypeId = dto.EventTypeId });

        // Edit link - requires Update permission
        yield return new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.UpdateEventTemplate,
            new { id = dto.Id },
            HttpMethods.Patch,
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.EventTemplate, dto);

        // Delete link - requires Delete permission
        yield return LinkDefinition.Delete(
            RouteNames.DeleteEventTemplate,
            new { id = dto.Id })
            .RequirePermission(AuthorizationActions.Delete, ResourceDescriptors.EventTemplate, dto);
    }
}

/// <summary>
/// Link policy for EventTemplateListDto (collection items).
/// </summary>
public sealed class EventTemplateCollectionLinkPolicy : ICollectionLinkPolicy<EventTemplateListDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(EventTemplateListDto dto, ClaimsPrincipal? user)
    {
        // Self link for the item
        yield return LinkDefinition.Self(
            RouteNames.GetEventTemplateById,
            new { id = dto.Id });

        yield return new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.UpdateEventTemplate,
            new { id = dto.Id },
            HttpMethods.Patch,
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.EventTemplateList, dto);

        yield return LinkDefinition.Delete(
            RouteNames.DeleteEventTemplate,
            new { id = dto.Id })
            .RequirePermission(AuthorizationActions.Delete, ResourceDescriptors.EventTemplateList, dto);
    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        // Create link - requires Create permission
        yield return LinkDefinition.Create(RouteNames.CreateEventTemplate)
            .RequirePermission(AuthorizationActions.Create, typeof(EventTemplateDto), "eventTemplate");
    }
}
