namespace Explore.API.Hateoas.Policies;

using System.Collections.Generic;
using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EventSessionAgendaItem;
using Explore.Application.Hateoas;

/// <summary>
/// Link policy for EventSessionAgendaItemDto (detail view).
/// Provides links for agenda item operations.
/// </summary>
public sealed class EventSessionAgendaItemDetailLinkPolicy : ILinkPolicy<EventSessionAgendaItemDto>
{
    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetLinks(EventSessionAgendaItemDto dto, ClaimsPrincipal? user)
    {
        // Self link
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetEventSessionAgendaItemById,
            new { id = dto.Id },
            "GET",
            dto.Title);

        // Event session link
        yield return new LinkDefinition(
            "event-session",
            RouteNames.GetEventSessionById,
            new { id = dto.EventSessionId },
            "GET",
            dto.EventSessionTitle);

        // Location link (if has location)
        if (dto.LocationId.HasValue)
        {
            yield return new LinkDefinition(
                "location",
                RouteNames.GetLocationById,
                new { id = dto.LocationId },
                "GET",
                dto.LocationFullName);
        }

        // Edit link - requires authentication
        yield return new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.UpdateEventSessionAgendaItem,
            new { id = dto.Id },
            HttpMethods.Patch,
            "Update agenda item",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.EventSessionAgendaItem, dto);

        // Delete link - requires authentication
        yield return new LinkDefinition(
            "delete",
            RouteNames.DeleteEventSessionAgendaItem,
            new { id = dto.Id },
            "DELETE",
            "Delete agenda item",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Delete, ResourceDescriptors.EventSessionAgendaItem, dto);
    }
}

/// <summary>
/// Link policy for EventSessionAgendaItemListDto (collection items).
/// </summary>
public sealed class EventSessionAgendaItemCollectionLinkPolicy : ICollectionLinkPolicy<EventSessionAgendaItemListDto>
{
    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetItemLinks(EventSessionAgendaItemListDto dto, ClaimsPrincipal? user)
    {
        // Self link for item
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetEventSessionAgendaItemById,
            new { id = dto.Id },
            "GET",
            dto.Title);

        // Event session link
        yield return new LinkDefinition(
            "event-session",
            RouteNames.GetEventSessionById,
            new { id = dto.EventSessionId },
            "GET",
            dto.EventSessionTitle);
    }

    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        // Create link - requires authentication
        yield return new LinkDefinition(
            "create",
            RouteNames.CreateEventSessionAgendaItem,
            null,
            "POST",
            "Create agenda item",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Create, typeof(EventSessionAgendaItemDto), "event_session_agenda_item");
    }
}
