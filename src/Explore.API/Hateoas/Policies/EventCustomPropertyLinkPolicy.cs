namespace Explore.API.Hateoas.Policies;

using System.Collections.Generic;
using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.Hateoas;

/// <summary>
/// Link policy for EventCustomPropertyDefinitionDto (detail view).
/// Determines which links should be included based on resource state and user authorization.
/// </summary>
public sealed class EventCustomPropertyDefinitionDetailLinkPolicy : ILinkPolicy<EventCustomPropertyDefinitionDto>
{
    public IEnumerable<LinkDefinition> GetLinks(EventCustomPropertyDefinitionDto dto, ClaimsPrincipal? user)
    {
        // Self link - always included
        yield return LinkDefinition.Self(
            RouteNames.GetEventCustomPropertyDefinitionById,
            new { id = dto.Id });

        // Collection link
        yield return LinkDefinition.Collection(
            RouteNames.GetEventCustomPropertyDefinitions,
            new { eventId = dto.EventId });

        // Values link
        yield return new LinkDefinition(
            "values",
            RouteNames.GetEventCustomPropertyValues,
            new { eventId = dto.EventId });

        // Edit link - requires Update permission
        yield return new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.UpdateEventCustomPropertyDefinition,
            new { id = dto.Id },
            HttpMethods.Patch,
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.EventCustomPropertyDefinition, dto);

        // Delete link - requires Delete permission
        yield return LinkDefinition.Delete(
            RouteNames.DeleteEventCustomPropertyDefinition,
            new { id = dto.Id })
            .RequirePermission(AuthorizationActions.Delete, ResourceDescriptors.EventCustomPropertyDefinition, dto);
    }
}

/// <summary>
/// Link policy for EventCustomPropertyDefinitionListDto (collection items).
/// </summary>
public sealed class EventCustomPropertyDefinitionCollectionLinkPolicy : ICollectionLinkPolicy<EventCustomPropertyDefinitionListDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(EventCustomPropertyDefinitionListDto dto, ClaimsPrincipal? user)
    {
        // Self link for the item
        yield return LinkDefinition.Self(
            RouteNames.GetEventCustomPropertyDefinitionById,
            new { id = dto.Id });
    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        // Create link - requires Create permission
        yield return LinkDefinition.Create(RouteNames.CreateEventCustomPropertyDefinition)
            .RequirePermission(AuthorizationActions.Create, typeof(EventCustomPropertyDefinitionDto), "eventCustomPropertyDefinition");
    }
}
