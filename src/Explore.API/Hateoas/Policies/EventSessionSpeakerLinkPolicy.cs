namespace Explore.API.Hateoas.Policies;

using System.Collections.Generic;
using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EventSessionSpeaker;
using Explore.Application.Hateoas;

public sealed class EventSessionSpeakerDetailLinkPolicy : ILinkPolicy<EventSessionSpeakerDto>
{
    public IEnumerable<LinkDefinition> GetLinks(EventSessionSpeakerDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            "event-session",
            RouteNames.GetEventSessionById,
            new { id = dto.EventSessionId },
            "GET",
            dto.EventSessionTitle);

        yield return new LinkDefinition(
            "actor",
            RouteNames.GetActorById,
            new { id = dto.ActorId },
            "GET",
            dto.ActorDisplayName);

        yield return new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.UpdateEventSessionSpeaker,
            new { id = dto.Id },
            "PATCH",
            "Update speaker assignment",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.EventSessionSpeaker, dto);

        yield return new LinkDefinition(
            "delete",
            RouteNames.DeleteEventSessionSpeaker,
            new { id = dto.Id },
            "DELETE",
            "Remove speaker assignment",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.EventSessionSpeaker, dto);
    }
}

public sealed class EventSessionSpeakerCollectionLinkPolicy : ICollectionLinkPolicy<EventSessionSpeakerListDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(EventSessionSpeakerListDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            "event-session",
            RouteNames.GetEventSessionById,
            new { id = dto.EventSessionId },
            "GET",
            dto.EventSessionTitle);

        yield return new LinkDefinition(
            "actor",
            RouteNames.GetActorById,
            new { id = dto.ActorId },
            "GET",
            dto.ActorDisplayName);

        yield return new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.UpdateEventSessionSpeaker,
            new { id = dto.Id },
            "PATCH",
            "Update speaker assignment",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.EventSessionSpeakerList, dto);

        yield return new LinkDefinition(
            "delete",
            RouteNames.DeleteEventSessionSpeaker,
            new { eventSessionId = dto.EventSessionId, id = dto.Id },
            "DELETE",
            "Remove speaker assignment",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.EventSessionSpeakerList, dto);
    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        return [];
    }
}
