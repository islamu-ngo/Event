namespace Explore.API.Hateoas.Policies;

using System.Collections.Generic;
using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EventDay;
using Explore.Application.Hateoas;

/// <summary>
/// Link policy for EventDayDto (detail view).
/// </summary>
public sealed class EventDayDetailLinkPolicy : ILinkPolicy<EventDayDto>
{
    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetLinks(EventDayDto dto, ClaimsPrincipal? user)
    {
        // Self link
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetEventDayById,
            new { id = dto.Id },
            "GET",
            dto.Label);

        // Parent event link
        yield return new LinkDefinition(
            "event",
            RouteNames.GetEventById,
            new { id = dto.EventId },
            "GET",
            dto.EventTitle);

        // Sibling days collection
        yield return new LinkDefinition(
            LinkRelations.Collection,
            RouteNames.GetEventDaysByEvent,
            new { eventId = dto.EventId },
            "GET",
            "Event days");

        // Edit link - requires authentication and permission
        yield return new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.UpdateEventDay,
            new { id = dto.Id },
            "PATCH",
            "Update event day",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.EventDay, dto);

        // Delete link - requires authentication and permission
        yield return new LinkDefinition(
            "delete",
            RouteNames.DeleteEventDay,
            new { id = dto.Id },
            "DELETE",
            "Delete event day",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Delete, ResourceDescriptors.EventDay, dto);
    }
}

/// <summary>
/// Link policy for EventDayListDto (collection items).
/// </summary>
public sealed class EventDayCollectionLinkPolicy : ICollectionLinkPolicy<EventDayListDto>
{
    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetItemLinks(EventDayListDto dto, ClaimsPrincipal? user)
    {
        // Self link for item
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetEventDayById,
            new { id = dto.Id },
            "GET",
            dto.Label);

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
            RouteNames.CreateEventDay,
            null,
            "POST",
            "Create new event day",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Create, typeof(EventDayDto), "event_day");
    }
}
