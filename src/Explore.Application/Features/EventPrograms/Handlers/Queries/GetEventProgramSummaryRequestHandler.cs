using System.Collections.Immutable;
using System.Globalization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventProgram;
using Explore.Application.DTOs.Location;
using Explore.Application.Features.EventPrograms.Models;
using Explore.Application.Features.EventPrograms.Requests.Queries;
using Explore.Application.Services;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.EventPrograms.Handlers.Queries;

public class GetEventProgramSummaryRequestHandler :
    IRequestHandler<GetEventProgramSummaryRequest, EventProgramSummaryDto?>,
    IRequestHandler<GetManagedEventProgramSummaryRequest, EventProgramSummaryDto?>
{
    private const string UnassignedSectionKey = "unassigned";
    private const int UnassignedSortOrder = int.MaxValue;

    private readonly IEventRepository _eventRepository;
    private readonly IEventSessionRepository _eventSessionRepository;
    private readonly IEventSessionGroupRepository _eventSessionGroupRepository;
    private readonly IEventAgendaItemRepository _eventAgendaItemRepository;
    private readonly IEventLocationDisclosureService _disclosureService;

    public GetEventProgramSummaryRequestHandler(
        IEventRepository eventRepository,
        IEventSessionRepository eventSessionRepository,
        IEventSessionGroupRepository eventSessionGroupRepository,
        IEventAgendaItemRepository eventAgendaItemRepository,
        IEventLocationDisclosureService disclosureService)
    {
        _eventRepository = eventRepository;
        _eventSessionRepository = eventSessionRepository;
        _eventSessionGroupRepository = eventSessionGroupRepository;
        _eventAgendaItemRepository = eventAgendaItemRepository;
        _disclosureService = disclosureService;
    }

    public async Task<EventProgramSummaryDto?> Handle(GetEventProgramSummaryRequest request, CancellationToken cancellationToken)
        => await BuildSummaryAsync(request.EventId, includeManaged: false, cancellationToken);

    public async Task<EventProgramSummaryDto?> Handle(GetManagedEventProgramSummaryRequest request, CancellationToken cancellationToken)
        => await BuildSummaryAsync(request.EventId, includeManaged: true, cancellationToken);

    private async Task<EventProgramSummaryDto?> BuildSummaryAsync(
        Guid eventId,
        bool includeManaged,
        CancellationToken cancellationToken)
    {
        var eventEntity = await _eventRepository.GetEventWithDetails(eventId);
        if (eventEntity is null)
            return null;

        if (!includeManaged && !await _eventRepository.IsPubliclyEligibleAsync(
                eventEntity.TenantId,
                eventEntity.Id,
                cancellationToken))
            return null;

        var sessions = includeManaged
            ? await _eventSessionRepository.GetSessionsByEvent(eventId)
            : await _eventSessionRepository.GetPublicSessionsByEventAsync(eventId, cancellationToken);
        var groups = includeManaged
            ? await _eventSessionGroupRepository.GetActiveByEventAsync(eventId, cancellationToken)
            : await _eventSessionGroupRepository.GetPublicByEventAsync(eventId, cancellationToken);
        var agendaItems = includeManaged
            ? await _eventAgendaItemRepository.GetByEventAsync(eventId, cancellationToken)
            : await _eventAgendaItemRepository.GetPublicByEventAsync(eventId, cancellationToken);
        var groupLookup = groups.ToDictionary(group => group.Id);
        IReadOnlyDictionary<Guid, EventLocationPublicDto> eventLocations = includeManaged
            ? ImmutableDictionary<Guid, EventLocationPublicDto>.Empty
            : await PublicEventLocationProjection.ResolveAsync(
                _disclosureService,
                sessions
                    .Select(session => new PublicEventLocationPlacement(
                        session.TenantId,
                        session.EventId,
                        session.EventLocationId,
                        session.RoomId))
                    .Concat(groups.Select(group => new PublicEventLocationPlacement(
                        group.TenantId,
                        group.EventId,
                        group.EventLocationId,
                        group.RoomId))),
                cancellationToken);
        var timezoneId = eventEntity.EventTimeZoneId ?? eventEntity.Timezone;
        var timezone = ResolveTimeZone(timezoneId);

        var readinessWarnings = new List<EventProgramReadinessWarningDto>();

        AddGlobalWarnings(readinessWarnings, eventEntity, timezoneId, groups, sessions);
        AddAgendaWarnings(readinessWarnings, agendaItems, eventEntity, timezone);

        var programGroups = BuildProgramGroups(
            sessions,
            groupLookup,
            eventLocations,
            timezone,
            eventEntity,
            readinessWarnings);

        return new EventProgramSummaryDto
        {
            EventId = eventEntity.Id,
            EventTitle = eventEntity.Title,
            TimeZoneId = timezoneId,
            ReadinessWarnings = readinessWarnings,
            Sections = programGroups
                .OrderBy(group => group.SortOrder)
                .ThenBy(group => group.Title)
                .Select(group => new EventProgramSectionDto
                {
                    SectionKey = group.SectionKey,
                    Title = group.Title,
                    SortOrder = group.SortOrder,
                    SessionGroups = [BuildSessionGroupSection(group)]
                })
                .ToList()
        };
    }

    private static List<ProgramGroupAccumulator> BuildProgramGroups(
        IEnumerable<EventSession> sessions,
        IReadOnlyDictionary<Guid, EventSessionGroup> groupLookup,
        IReadOnlyDictionary<Guid, EventLocationPublicDto> eventLocations,
        TimeZoneInfo timezone,
        Event eventEntity,
        List<EventProgramReadinessWarningDto> readinessWarnings)
    {
        var accumulators = new Dictionary<string, ProgramGroupAccumulator>(StringComparer.Ordinal);

        foreach (var (session, sessionIndex) in sessions.Select((session, index) => (session, index)))
        {
            var primaryAssignment = session.SessionGroups
                .Where(assignment => !assignment.IsDeleted && groupLookup.ContainsKey(assignment.EventSessionGroupId))
                .OrderByDescending(assignment => assignment.IsPrimary)
                .ThenBy(assignment => assignment.SortOrder)
                .FirstOrDefault();
            var group = primaryAssignment is null ? null : groupLookup[primaryAssignment.EventSessionGroupId];
            var sectionKey = group?.Id.ToString() ?? UnassignedSectionKey;

            if (!accumulators.TryGetValue(sectionKey, out var accumulator))
            {
                accumulator = group is null
                    ? ProgramGroupAccumulator.Unassigned(UnassignedSectionKey, UnassignedSortOrder)
                    : ProgramGroupAccumulator.FromGroup(
                        group,
                        group.EventLocationId is { } groupEventLocationId
                            ? eventLocations.GetValueOrDefault(groupEventLocationId)
                            : null);
                accumulators[sectionKey] = accumulator;
            }

            var item = BuildProgramItem(
                session,
                primaryAssignment,
                group,
                session.EventLocationId is { } sessionEventLocationId
                    ? eventLocations.GetValueOrDefault(sessionEventLocationId)
                    : null,
                timezone,
                eventEntity);
            var itemWarnings = new List<EventProgramReadinessWarningDto>();

            if (primaryAssignment is null)
            {
                AddWarning(readinessWarnings, ProgramSessionPath(sessionIndex, "sessionGroupId"), "Assign this program item to a section, track, devroom, or stage before publishing.");
                itemWarnings.Add(CreateWarning(ProgramSessionPath(sessionIndex, "sessionGroupId"), "Program section is not assigned."));
            }

            AddSessionWarnings(readinessWarnings, itemWarnings, item, session, eventEntity, sessionIndex);
            accumulator.Items.Add(item with { ReadinessWarnings = itemWarnings });
        }

        return accumulators.Values.ToList();
    }

    private static EventProgramItemDto BuildProgramItem(
        EventSession session,
        EventSessionGroupSession? assignment,
        EventSessionGroup? group,
        EventLocationPublicDto? eventLocation,
        TimeZoneInfo timezone,
        Event eventEntity)
    {
        var startTime = session.StartTime;
        var endTime = session.EndTime;

        var localStart = startTime is null
            ? (DateTimeOffset?)null
            : session.LocalStartDate is null || session.LocalStartTime is null
            ? TimeZoneInfo.ConvertTime(startTime.Value, timezone)
            : new DateTimeOffset(
                session.LocalStartDate.Value.ToDateTime(session.LocalStartTime.Value),
                TimeZoneInfo.ConvertTime(startTime.Value, timezone).Offset);
        var localEnd = endTime is null
            ? (DateTimeOffset?)null
            : session.LocalEndDate is null || session.LocalEndTime is null
            ? TimeZoneInfo.ConvertTime(endTime.Value, timezone)
            : new DateTimeOffset(
                session.LocalEndDate.Value.ToDateTime(session.LocalEndTime.Value),
                TimeZoneInfo.ConvertTime(endTime.Value, timezone).Offset);

        return new EventProgramItemDto
        {
            SessionId = session.Id,
            Title = string.IsNullOrWhiteSpace(session.Title) ? "Untitled program item" : session.Title.Trim(),
            EventSessionKindId = session.EventSessionKindId,
            EventSessionKindName = session.EventSessionKind?.FullName,
            EventSessionKindMasterCode = session.EventSessionKind?.MasterCode,
            StartsAtUtc = startTime,
            EndsAtUtc = endTime,
            LocalDate = localStart is null ? null : DateOnly.FromDateTime(localStart.Value.DateTime),
            LocalStartTime = localStart is null ? null : TimeOnly.FromDateTime(localStart.Value.DateTime),
            LocalEndTime = localEnd is null ? null : TimeOnly.FromDateTime(localEnd.Value.DateTime),
            SortOrder = assignment?.SortOrder ?? session.SortOrder,
            SessionGroupId = group?.Id,
            LocationName = null,
            RoomName = null,
            EventLocation = eventLocation,
            Capacity = session.MaxAudienceAttendees,
            RegistrationModeName = session.RegistrationMode?.FullName
        };
    }

    private static EventProgramSessionGroupSectionDto BuildSessionGroupSection(ProgramGroupAccumulator accumulator)
    {
        return new EventProgramSessionGroupSectionDto
        {
            SessionGroupId = accumulator.SessionGroupId,
            Title = accumulator.Title,
            SortOrder = accumulator.SortOrder,
            Color = accumulator.Color,
            LocationName = null,
            RoomName = null,
            EventLocation = accumulator.EventLocation,
            Days = accumulator.Items
                .GroupBy(item => item.LocalDate)
                .OrderBy(group => group.Key)
                .Select(group => new EventProgramDayGroupDto
                {
                    LocalDate = group.Key,
                    DisplayLabel = group.Key?.ToString("ddd d MMM", CultureInfo.InvariantCulture) ?? "Unscheduled",
                    Items = group
                        .OrderBy(item => item.SortOrder)
                        .ThenBy(item => item.LocalStartTime)
                        .ThenBy(item => item.Title)
                        .ToList()
                })
                .ToList()
        };
    }

    private static void AddGlobalWarnings(
        List<EventProgramReadinessWarningDto> readinessWarnings,
        Event eventEntity,
        string? timezoneId,
        IReadOnlyCollection<EventSessionGroup> groups,
        IReadOnlyCollection<EventSession> sessions)
    {
        if (string.IsNullOrWhiteSpace(timezoneId))
            AddWarning(readinessWarnings, "event.timeZoneId", "No event timezone is configured. Program summary uses UTC until a timezone is set.");

        if (!eventEntity.FirstSessionDate.HasValue || !eventEntity.LastSessionDate.HasValue)
            AddWarning(readinessWarnings, "event.dateWindow", "No event date window is configured. Confirm program item dates before publishing.");

        if (groups.Count == 0)
            AddWarning(readinessWarnings, "program.groups", "No program sections exist yet. Add a section, track, devroom, or stage to organize sessions.");

        if (sessions.Count == 0)
            AddWarning(readinessWarnings, "program.sessions", "No program items exist yet. Add talks, workshops, panels, classes, or activities as sessions.");
    }

    private static void AddSessionWarnings(
        List<EventProgramReadinessWarningDto> readinessWarnings,
        List<EventProgramReadinessWarningDto> itemWarnings,
        EventProgramItemDto item,
        EventSession session,
        Event eventEntity,
        int sessionIndex)
    {
        if (string.IsNullOrWhiteSpace(session.Title))
        {
            AddWarning(readinessWarnings, ProgramSessionPath(sessionIndex, "title"), "A program item is missing a title.");
            itemWarnings.Add(CreateWarning(ProgramSessionPath(sessionIndex, "title"), "Title is missing."));
        }

        if (session.MaxAudienceAttendees is null)
            AddWarning(readinessWarnings, ProgramSessionPath(sessionIndex, "maxAudienceAttendees"), $"{item.Title} has no capacity configured.");

        if (session.RegistrationModeId is null)
            AddWarning(readinessWarnings, ProgramSessionPath(sessionIndex, "registrationModeId"), $"{item.Title} has no registration mode configured.");

        if (session.StartTime is null)
        {
            AddWarning(readinessWarnings, ProgramSessionPath(sessionIndex, "startTime"), $"{item.Title} is not scheduled yet.");
            itemWarnings.Add(CreateWarning(ProgramSessionPath(sessionIndex, "startTime"), "Start time is not scheduled."));
        }

        if (session.EndTime is null)
        {
            AddWarning(readinessWarnings, ProgramSessionPath(sessionIndex, "endTime"), $"{item.Title} has no end time yet.");
            itemWarnings.Add(CreateWarning(ProgramSessionPath(sessionIndex, "endTime"), "End time is not scheduled."));
        }

        if (item.LocalDate.HasValue &&
            (eventEntity.FirstSessionDate.HasValue && item.LocalDate.Value < eventEntity.FirstSessionDate.Value ||
             eventEntity.LastSessionDate.HasValue && item.LocalDate.Value > eventEntity.LastSessionDate.Value))
        {
            AddWarning(readinessWarnings, ProgramSessionPath(sessionIndex, "startTime"), $"{item.Title} is outside the event date window.");
            itemWarnings.Add(CreateWarning(ProgramSessionPath(sessionIndex, "startTime"), "Outside event date window."));
        }
    }

    private static void AddAgendaWarnings(
        List<EventProgramReadinessWarningDto> readinessWarnings,
        IReadOnlyList<EventAgendaItem> agendaItems,
        Event eventEntity,
        TimeZoneInfo timezone)
    {
        foreach (var (agendaItem, agendaIndex) in agendaItems.Select((agendaItem, index) => (agendaItem, index)))
        {
            var localStart = agendaItem.LocalStartDate == default
                ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(agendaItem.StartTime, timezone).DateTime)
                : agendaItem.LocalStartDate;

            if (eventEntity.FirstSessionDate.HasValue && localStart < eventEntity.FirstSessionDate.Value ||
                eventEntity.LastSessionDate.HasValue && localStart > eventEntity.LastSessionDate.Value)
            {
                AddWarning(readinessWarnings, ProgramAgendaPath(agendaIndex, "startTime"), $"{agendaItem.Title} is outside the event date window.");
            }
        }
    }

    private static TimeZoneInfo ResolveTimeZone(string? timezoneId)
    {
        if (string.IsNullOrWhiteSpace(timezoneId))
            return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static void AddWarning(List<EventProgramReadinessWarningDto> readinessWarnings, string path, string message)
    {
        readinessWarnings.Add(CreateWarning(path, message));
    }

    private static EventProgramReadinessWarningDto CreateWarning(string path, string message)
    {
        return new EventProgramReadinessWarningDto
        {
            Path = path,
            Message = message
        };
    }

    private static string ProgramSessionPath(int index, string propertyName)
    {
        return $"program.sessions[{index}].{propertyName}";
    }

    private static string ProgramAgendaPath(int index, string propertyName)
    {
        return $"program.agenda[{index}].{propertyName}";
    }

}
