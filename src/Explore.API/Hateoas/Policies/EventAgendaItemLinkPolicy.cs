namespace Explore.API.Hateoas.Policies;

using System.Collections.Generic;
using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EventAgendaItem;
using Explore.Application.Hateoas;

/// <summary>
/// Link policy for EventAgendaItemDto (detail view).
/// </summary>
public sealed class EventAgendaItemDetailLinkPolicy : ILinkPolicy<EventAgendaItemDto>
{
    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetLinks(EventAgendaItemDto dto, ClaimsPrincipal? user)
    {
        // Self link
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetEventAgendaItemById,
            new { id = dto.Id },
            "GET",
            dto.Title);

        // Parent event link
        yield return new LinkDefinition(
            "event",
            RouteNames.GetEventById,
            new { id = dto.EventId },
            "GET",
            dto.EventTitle);

        // Sibling agenda items collection
        yield return new LinkDefinition(
            LinkRelations.Collection,
            RouteNames.GetEventAgendaItemsByEvent,
            new { eventId = dto.EventId },
            "GET",
            "Event agenda items");

        // Agenda projection link
        yield return new LinkDefinition(
            "agenda-projection",
            RouteNames.GetEventAgendaProjection,
            new { eventId = dto.EventId },
            "GET",
            "Full agenda projection");

        // Edit link - requires authentication and permission
        yield return new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.UpdateEventAgendaItem,
            new { id = dto.Id },
            "PATCH",
            "Update agenda item",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.EventAgendaItem, dto);

        // Delete link - requires authentication and permission
        yield return new LinkDefinition(
            "delete",
            RouteNames.DeleteEventAgendaItem,
            new { id = dto.Id },
            "DELETE",
            "Delete agenda item",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Delete, ResourceDescriptors.EventAgendaItem, dto);
    }
}

/// <summary>
/// Link policy for EventAgendaItemListDto (collection items).
/// </summary>
public sealed class EventAgendaItemCollectionLinkPolicy : ICollectionLinkPolicy<EventAgendaItemListDto>
{
    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetItemLinks(EventAgendaItemListDto dto, ClaimsPrincipal? user)
    {
        // Self link for item
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetEventAgendaItemById,
            new { id = dto.Id },
            "GET",
            dto.Title);

        // Parent event link
        yield return new LinkDefinition(
            "event",
            RouteNames.GetEventById,
            new { id = dto.EventId },
            "GET",
            "Parent event");
    }

    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        // Create link - requires authentication
        yield return new LinkDefinition(
            "create",
            RouteNames.CreateEventAgendaItem,
            null,
            "POST",
            "Create new agenda item",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Create, typeof(EventAgendaItemDto), "event_agenda_item");
    }
}
