using Explore.Application.Contracts.LocationPrivacy;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Event;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.Events.Handlers.Queries;

public sealed class GetEventCalendarExportRequestHandler(
    IEventRepository eventRepository,
    IEventSessionRepository eventSessionRepository,
    IEventLocationDisclosureService eventLocationDisclosureService)
    : IRequestHandler<GetEventCalendarExportRequest, EventCalendarExportDto?>
{
    public async Task<EventCalendarExportDto?> Handle(
        GetEventCalendarExportRequest request,
        CancellationToken cancellationToken)
    {
        Event? entity = await eventRepository.GetEventWithDetails(request.EventId);
        if (entity is null || !IsPublicCalendarEligible(entity))
        {
            return null;
        }

        List<EventSession> sessions = await eventSessionRepository.GetPublicSessionsByEventAsync(
            request.EventId,
            cancellationToken);
        EventSession? primarySession = sessions
            .OrderBy(session => session.StartTime)
            .FirstOrDefault();

        if (primarySession is null)
        {
            return null;
        }

        if (primarySession.StartTime is null || primarySession.EndTime is null)
        {
            return null;
        }

        string? location = await ResolvePublicLocationAsync(primarySession, cancellationToken);

        return new EventCalendarExportDto(
            entity.Id,
            entity.Title,
            entity.Content ?? entity.Description,
            entity.Slug,
            primarySession.StartTime.Value.ToUniversalTime(),
            primarySession.EndTime.Value.ToUniversalTime(),
            location);
    }

    private async Task<string?> ResolvePublicLocationAsync(
        EventSession session,
        CancellationToken cancellationToken)
    {
        if (session.EventLocationId is not { } eventLocationId)
        {
            return null;
        }

        IReadOnlyDictionary<Guid, EventLocationDisclosureResult> results =
            await eventLocationDisclosureService.ResolveManyAsync(
                [new EventLocationDisclosureRequest(
                    session.TenantId,
                    session.EventId,
                    eventLocationId,
                    session.RoomId,
                    RequesterUserId: null,
                    EventLocationDisclosurePurpose.Public)],
                cancellationToken);

        return results.TryGetValue(eventLocationId, out EventLocationDisclosureResult? result)
            && result.Purpose == EventLocationDisclosurePurpose.Public
            && result.State != EventLocationDisclosureState.Hidden
                ? FormatPublicLocation(result.Values)
                : null;
    }

    private static string? FormatPublicLocation(EventLocationDisclosureValues? values)
    {
        if (values is null)
        {
            return null;
        }

        string[] parts =
        [
            values.VenueName,
            values.RoomName,
            values.StreetAddress,
            values.Postcode,
            values.City,
            values.Country
        ];
        string location = string.Join(", ", parts.Where(value => !string.IsNullOrWhiteSpace(value))!);
        return string.IsNullOrWhiteSpace(location) ? null : location;
    }

    private static bool IsPublicCalendarEligible(Event entity)
    {
        return entity.EventStatusId == (int)EventStatusEnum.Published &&
            entity.VisibilityTypeId == (int)VisibilityTypeEnum.Public;
    }

}
