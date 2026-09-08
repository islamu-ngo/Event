namespace Explore.API.Hateoas.Policies;

using System.Collections.Generic;
using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EventSession;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;
using Explore.Domain.Services.Lifecycle;

/// <summary>
/// Link policy for EventSessionDto (detail view).
/// </summary>
public sealed class EventSessionDetailLinkPolicy : ILinkPolicy<EventSessionDto>
{
    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetLinks(EventSessionDto dto, ClaimsPrincipal? user)
    {
        // Self link
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetEventSessionById,
            new { id = dto.Id },
            "GET",
            dto.Title ?? "Session details");

        // Parent event link
        yield return new LinkDefinition(
            "event",
            RouteNames.GetEventById,
            new { id = dto.EventId },
            "GET",
            dto.EventTitle);

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

        // Agenda items link
        yield return new LinkDefinition(
            "agenda-items",
            RouteNames.GetEventSessionAgendaItems,
            new { sessionId = dto.Id },
            "GET",
            "Session agenda");

        yield return new LinkDefinition(
            "speakers",
            RouteNames.GetEventSessionSpeakersBySession,
            new { eventSessionId = dto.Id },
            "GET",
            "Session speakers",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.EventSession, dto);

        foreach (var assignment in dto.SessionGroups)
        {
            yield return new LinkDefinition(
                LinkRelations.SessionGroups,
                RouteNames.GetEventSessionGroupById,
                new { id = assignment.EventSessionGroupId },
                "GET",
                assignment.Name);
        }

        // Edit link - requires authentication
        yield return new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.UpdateEventSession,
            new { id = dto.Id },
            "PATCH",
            "Update session",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.EventSession, dto);

        var sessionStatus = (EventSessionStatusEnum)dto.EventSessionStatusId;
        var parentStatus = (EventStatusEnum)dto.ParentEventStatusId;

        if (EventSessionLifecycleRules.CanSchedule(sessionStatus))
        {
            yield return new LinkDefinition(
                LinkRelations.Schedule,
                RouteNames.ScheduleEventSession,
                new { id = dto.Id },
                "POST",
                "Schedule session",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.EventSession, dto);
        }

        if (EventSessionLifecycleRules.CanPublish(sessionStatus, parentStatus, dto.StartTime, dto.EndTime, dto.EndTimeType))
        {
            yield return new LinkDefinition(
                LinkRelations.Publish,
                RouteNames.PublishEventSession,
                new { id = dto.Id },
                "POST",
                "Publish session",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.EventSession, dto);
        }

        if (EventSessionLifecycleRules.CanCancel(sessionStatus, parentStatus))
        {
            yield return EventSessionLifecycleLinkFactory.Create(LinkRelations.Cancel, dto, "Cancel session", RouteNames.CancelEventSession);
        }

        if (EventSessionLifecycleRules.CanComplete(sessionStatus, parentStatus))
        {
            yield return EventSessionLifecycleLinkFactory.Create(LinkRelations.Complete, dto, "Complete session", RouteNames.CompleteEventSession);
        }

        if (EventSessionLifecycleRules.CanArchive(sessionStatus, parentStatus))
        {
            yield return EventSessionLifecycleLinkFactory.Create(LinkRelations.Archive, dto, "Archive session", RouteNames.ArchiveEventSession);
        }

        // Delete link - requires authentication
        yield return new LinkDefinition(
            "delete",
            RouteNames.DeleteEventSession,
            new { id = dto.Id },
            "DELETE",
            "Delete session",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Delete, ResourceDescriptors.EventSession, dto);
    }
}

/// <summary>
/// Link policy for EventSessionListDto (collection items).
/// </summary>
public sealed class EventSessionCollectionLinkPolicy : ICollectionLinkPolicy<EventSessionListDto>
{
    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetItemLinks(EventSessionListDto dto, ClaimsPrincipal? user)
    {
        // Self link for item
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetEventSessionById,
            new { id = dto.Id },
            "GET",
            dto.Title ?? "Session");

        // Parent event link
        yield return new LinkDefinition(
            "event",
            RouteNames.GetEventById,
            new { id = dto.EventId },
            "GET",
            dto.EventTitle);

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

        foreach (var assignment in dto.SessionGroups)
        {
            yield return new LinkDefinition(
                LinkRelations.SessionGroups,
                RouteNames.GetEventSessionGroupById,
                new { id = assignment.EventSessionGroupId },
                "GET",
                assignment.Name);
        }

        yield return new LinkDefinition(
            "speakers",
            RouteNames.GetEventSessionSpeakersBySession,
            new { eventSessionId = dto.Id },
            "GET",
            "Session speakers",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.EventSessionList, dto);

        var sessionStatus = (EventSessionStatusEnum)dto.EventSessionStatusId;
        var parentStatus = (EventStatusEnum)dto.ParentEventStatusId;

        if (EventSessionLifecycleRules.CanSchedule(sessionStatus))
        {
            yield return new LinkDefinition(
                LinkRelations.Schedule,
                RouteNames.ScheduleEventSession,
                new { id = dto.Id },
                "POST",
                "Schedule session",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.EventSessionList, dto);
        }

        if (EventSessionLifecycleRules.CanPublish(sessionStatus, parentStatus, dto.StartTime, dto.EndTime, dto.EndTimeType))
        {
            yield return new LinkDefinition(
                LinkRelations.Publish,
                RouteNames.PublishEventSession,
                new { id = dto.Id },
                "POST",
                "Publish session",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.EventSessionList, dto);
        }

        if (EventSessionLifecycleRules.CanCancel(sessionStatus, parentStatus))
        {
            yield return EventSessionLifecycleLinkFactory.Create(LinkRelations.Cancel, dto, "Cancel session", RouteNames.CancelEventSession);
        }

        if (EventSessionLifecycleRules.CanComplete(sessionStatus, parentStatus))
        {
            yield return EventSessionLifecycleLinkFactory.Create(LinkRelations.Complete, dto, "Complete session", RouteNames.CompleteEventSession);
        }

        if (EventSessionLifecycleRules.CanArchive(sessionStatus, parentStatus))
        {
            yield return EventSessionLifecycleLinkFactory.Create(LinkRelations.Archive, dto, "Archive session", RouteNames.ArchiveEventSession);
        }
    }

    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        // Create link - requires authentication
        yield return new LinkDefinition(
            "create",
            RouteNames.CreateEventSession,
            null,
            "POST",
            "Create new session",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Create, typeof(EventSessionDto), "event_session");
    }

}

internal static class EventSessionLifecycleLinkFactory
{
    public static LinkDefinition Create(
        string relation,
        EventSessionDto dto,
        string title,
        string routeName) =>
        new LinkDefinition(relation, routeName, new { id = dto.Id }, "POST", title, RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.EventSession, dto);

    public static LinkDefinition Create(
        string relation,
        EventSessionListDto dto,
        string title,
        string routeName) =>
        new LinkDefinition(relation, routeName, new { id = dto.Id }, "POST", title, RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.EventSessionList, dto);
}
