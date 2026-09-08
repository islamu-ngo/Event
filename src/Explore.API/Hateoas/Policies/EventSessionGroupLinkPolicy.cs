namespace Explore.API.Hateoas.Policies;

using System.Collections.Generic;
using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EventSessionGroup;
using Explore.Application.Hateoas;

/// <summary>
/// Link policy for EventSessionGroupDto detail views.
/// </summary>
public sealed class EventSessionGroupDetailLinkPolicy : ILinkPolicy<EventSessionGroupDto>
{
    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetLinks(EventSessionGroupDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetEventSessionGroupById,
            new { id = dto.Id },
            "GET",
            dto.Name);

        yield return new LinkDefinition(
            LinkRelations.Event,
            RouteNames.GetEventById,
            new { id = dto.EventId },
            "GET",
            dto.EventTitle ?? "Event");

        yield return new LinkDefinition(
            LinkRelations.Collection,
            RouteNames.GetEventSessionGroupsByEvent,
            new { eventId = dto.EventId },
            "GET",
            "Program sections");

        yield return new LinkDefinition(
            LinkRelations.Sessions,
            RouteNames.GetEventSessionGroupSessions,
            new { id = dto.Id },
            "GET",
            "Sessions in this program section");

        yield return new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.UpdateEventSessionGroup,
            new { id = dto.Id },
            HttpMethods.Patch,
            "Update program section",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.EventSessionGroup, dto);

        yield return new LinkDefinition(
            LinkRelations.Delete,
            RouteNames.DeleteEventSessionGroup,
            new { id = dto.Id, eventId = dto.EventId },
            "DELETE",
            "Delete program section",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Delete, ResourceDescriptors.EventSessionGroup, dto);

        yield return new LinkDefinition(
            LinkRelations.AssignSession,
            RouteNames.AssignEventSessionToGroup,
            new { id = dto.Id },
            "POST",
            "Assign a session to this program section",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.EventSessionGroup, dto);
    }
}

/// <summary>
/// Link policy for EventSessionGroupListDto collection items.
/// </summary>
public sealed class EventSessionGroupCollectionLinkPolicy : ICollectionLinkPolicy<EventSessionGroupListDto>
{
    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetItemLinks(EventSessionGroupListDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetEventSessionGroupById,
            new { id = dto.Id },
            "GET",
            dto.Name);

        yield return new LinkDefinition(
            LinkRelations.Event,
            RouteNames.GetEventById,
            new { id = dto.EventId },
            "GET",
            "Event");

        yield return new LinkDefinition(
            LinkRelations.Sessions,
            RouteNames.GetEventSessionGroupSessions,
            new { id = dto.Id },
            "GET",
            "Sessions in this program section");

        yield return new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.UpdateEventSessionGroup,
            new { id = dto.Id },
            HttpMethods.Patch,
            "Update program section",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.EventSessionGroupList, dto);

        yield return new LinkDefinition(
            LinkRelations.Delete,
            RouteNames.DeleteEventSessionGroup,
            new { id = dto.Id, eventId = dto.EventId },
            "DELETE",
            "Delete program section",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Delete, ResourceDescriptors.EventSessionGroupList, dto);

        yield return new LinkDefinition(
            LinkRelations.AssignSession,
            RouteNames.AssignEventSessionToGroup,
            new { id = dto.Id },
            "POST",
            "Assign sessions to this program section",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.EventSessionGroupList, dto);
    }
}
