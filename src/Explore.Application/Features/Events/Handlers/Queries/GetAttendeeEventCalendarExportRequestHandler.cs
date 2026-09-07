using Explore.Application.Contracts.LocationPrivacy;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Event;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.Events.Handlers.Queries;

public sealed class GetAttendeeEventCalendarExportRequestHandler(
    IEventRepository eventRepository,
    IEventSessionRepository eventSessionRepository,
    IEventLocationDisclosureService eventLocationDisclosureService)
    : IRequestHandler<GetAttendeeEventCalendarExportRequest, AttendeeEventCalendarExportDto?>
{
    public async Task<AttendeeEventCalendarExportDto?> Handle(
        GetAttendeeEventCalendarExportRequest request,
        CancellationToken cancellationToken)
    {
        Event? entity = await eventRepository.GetEventWithDetails(request.EventId);
        if (entity is null || entity.EventStatusId != (int)EventStatusEnum.Published)
        {
            return null;
        }

        EventSession? primarySession = (await eventSessionRepository.GetSessionsByEvent(request.EventId))
            .Where(session => session.ContributesToPublicScheduleSummary())
            .OrderBy(session => session.StartTime)
            .FirstOrDefault();
        if (primarySession?.EventLocationId is not { } eventLocationId
            || primarySession.StartTime is null
            || primarySession.EndTime is null)
        {
            return null;
        }

        IReadOnlyDictionary<Guid, EventLocationDisclosureResult> results =
            await eventLocationDisclosureService.ResolveManyAsync(
                [new EventLocationDisclosureRequest(
                    primarySession.TenantId,
                    primarySession.EventId,
                    eventLocationId,
                    primarySession.RoomId,
                    RequesterUserId: null,
                    EventLocationDisclosurePurpose.Attendee)],
                cancellationToken);
        if (!results.TryGetValue(eventLocationId, out EventLocationDisclosureResult? disclosure)
            || disclosure.Purpose != EventLocationDisclosurePurpose.Attendee
            || disclosure.State == EventLocationDisclosureState.Hidden)
        {
            return null;
        }

        return new AttendeeEventCalendarExportDto(
            entity.Id,
            entity.Title,
            entity.Content ?? entity.Description,
            entity.Slug,
            primarySession.StartTime.Value.ToUniversalTime(),
            primarySession.EndTime.Value.ToUniversalTime(),
            FormatAttendeeLocation(disclosure.Values));
    }

    private static string? FormatAttendeeLocation(EventLocationDisclosureValues? values)
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
}
