using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Event;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;

namespace Explore.API.Hateoas.Policies;

public sealed class EventPublicActionDetailLinkPolicy : ILinkPolicy<EventPublicActionDto>
{
    private const string EventDetailSurface = "event_detail";

    public IEnumerable<LinkDefinition> GetLinks(EventPublicActionDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetEventPublicAction,
            new { eventId = dto.EventId, actionId = dto.Id },
            HttpMethods.Get,
            dto.Label);
        yield return new LinkDefinition(
            LinkRelations.Event,
            RouteNames.GetEventById,
            new { id = dto.EventId },
            HttpMethods.Get,
            "Event");
        yield return DestinationLink(dto);
        yield return new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.UpdateEventPublicAction,
            new { eventId = dto.EventId, actionId = dto.Id },
            HttpMethods.Put,
            "Update public action",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Events.ManagePublicActions, ResourceDescriptors.EventPublicAction, dto);
        yield return new LinkDefinition(
            LinkRelations.Delete,
            RouteNames.DeleteEventPublicAction,
            new { eventId = dto.EventId, actionId = dto.Id },
            HttpMethods.Delete,
            "Delete public action",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Events.ManagePublicActions, ResourceDescriptors.EventPublicAction, dto);
    }

    private static LinkDefinition DestinationLink(EventPublicActionDto dto) => new(
        GetDestinationRelation(dto.KindId),
        RouteNames.RedirectEventPublicAction,
        new { eventId = dto.EventId, actionId = dto.Id, surface = EventDetailSurface },
        HttpMethods.Get,
        dto.Label ?? dto.KindName ?? "Open external destination");

    private static string GetDestinationRelation(int kindId) => (EventPublicActionKindEnum)kindId switch
    {
        EventPublicActionKindEnum.OriginalSource => LinkRelations.ViewOriginalSource,
        EventPublicActionKindEnum.ExternalEventPage => LinkRelations.ExternalEventPage,
        EventPublicActionKindEnum.ExternalRegistration => LinkRelations.ExternalRegistration,
        EventPublicActionKindEnum.OptionalQuestionnaire => LinkRelations.OptionalQuestionnaire,
        EventPublicActionKindEnum.Livestream => LinkRelations.Livestream,
        EventPublicActionKindEnum.OrganizerContact => LinkRelations.OrganizerContact,
        _ => LinkRelations.Related
    };
}

public sealed class EventPublicActionCollectionLinkPolicy : ICollectionLinkPolicy<EventPublicActionDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(EventPublicActionDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetEventPublicAction,
            new { eventId = dto.EventId, actionId = dto.Id },
            HttpMethods.Get,
            dto.Label);
        yield return new LinkDefinition(
            LinkRelations.Event,
            RouteNames.GetEventById,
            new { id = dto.EventId },
            HttpMethods.Get,
            "Event");
        yield return new LinkDefinition(
            GetDestinationRelation(dto.KindId),
            RouteNames.RedirectEventPublicAction,
            new { eventId = dto.EventId, actionId = dto.Id },
            HttpMethods.Get,
            dto.Label ?? dto.KindName ?? "Open external destination");
        yield return new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.UpdateEventPublicAction,
            new { eventId = dto.EventId, actionId = dto.Id },
            HttpMethods.Put,
            "Update public action",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Events.ManagePublicActions, ResourceDescriptors.EventPublicAction, dto);
        yield return new LinkDefinition(
            LinkRelations.Delete,
            RouteNames.DeleteEventPublicAction,
            new { eventId = dto.EventId, actionId = dto.Id },
            HttpMethods.Delete,
            "Delete public action",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Events.ManagePublicActions, ResourceDescriptors.EventPublicAction, dto);
    }

    private static string GetDestinationRelation(int kindId) => (EventPublicActionKindEnum)kindId switch
    {
        EventPublicActionKindEnum.OriginalSource => LinkRelations.ViewOriginalSource,
        EventPublicActionKindEnum.ExternalEventPage => LinkRelations.ExternalEventPage,
        EventPublicActionKindEnum.ExternalRegistration => LinkRelations.ExternalRegistration,
        EventPublicActionKindEnum.OptionalQuestionnaire => LinkRelations.OptionalQuestionnaire,
        EventPublicActionKindEnum.Livestream => LinkRelations.Livestream,
        EventPublicActionKindEnum.OrganizerContact => LinkRelations.OrganizerContact,
        _ => LinkRelations.Related
    };
}
