namespace Explore.API.Hateoas.Policies;

using System.Collections.Generic;
using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EventSessionCustomProperty;
using Explore.Application.Hateoas;

/// <summary>
/// Link policy for EventSessionCustomPropertyDefinitionDto (detail view).
/// Determines which links should be included based on resource state and user authorization.
/// </summary>
public sealed class EventSessionCustomPropertyDefinitionDetailLinkPolicy : ILinkPolicy<EventSessionCustomPropertyDefinitionDto>
{
    public IEnumerable<LinkDefinition> GetLinks(EventSessionCustomPropertyDefinitionDto dto, ClaimsPrincipal? user)
    {
        // Self link - always included
        yield return LinkDefinition.Self(
            RouteNames.GetEventSessionCustomPropertyDefinitionById,
            new { id = dto.Id });

        // Collection link
        yield return LinkDefinition.Collection(
            RouteNames.GetEventSessionCustomPropertyDefinitions,
            new { eventSessionId = dto.EventSessionId });

        // Values link
        yield return new LinkDefinition(
            "values",
            RouteNames.GetEventSessionCustomPropertyValues,
            new { eventSessionId = dto.EventSessionId });

        // Edit link - requires Update permission
        yield return new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.UpdateEventSessionCustomPropertyDefinition,
            new { id = dto.Id },
            HttpMethods.Patch,
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.EventSessionCustomPropertyDefinition, dto);

        // Delete link - requires Delete permission
        yield return LinkDefinition.Delete(
            RouteNames.DeleteEventSessionCustomPropertyDefinition,
            new { id = dto.Id })
            .RequirePermission(AuthorizationActions.Delete, ResourceDescriptors.EventSessionCustomPropertyDefinition, dto);
    }
}

/// <summary>
/// Link policy for EventSessionCustomPropertyDefinitionListDto (collection items).
/// </summary>
public sealed class EventSessionCustomPropertyDefinitionCollectionLinkPolicy : ICollectionLinkPolicy<EventSessionCustomPropertyDefinitionListDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(EventSessionCustomPropertyDefinitionListDto dto, ClaimsPrincipal? user)
    {
        // Self link for the item
        yield return LinkDefinition.Self(
            RouteNames.GetEventSessionCustomPropertyDefinitionById,
            new { id = dto.Id });
    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        // Create link - requires Create permission
        yield return LinkDefinition.Create(RouteNames.CreateEventSessionCustomPropertyDefinition)
            .RequirePermission(AuthorizationActions.Create, typeof(EventSessionCustomPropertyDefinitionDto), "eventSessionCustomPropertyDefinition");
    }
}
