using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Helpers;

namespace Explore.Blazor.Client.Pages.Events.Sessions;

internal static class EventSessionFormModelMapper
{
    public static EventSessionCreateFormState ApplyCreateContext(
        CreateEventSessionDto session,
        EventSessionCreateContextDto context,
        int fallbackRegistrationModeId)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(context);

        session.RegistrationModeId = context.Defaults?.RegistrationModeId > 0
            ? context.Defaults.RegistrationModeId
            : fallbackRegistrationModeId;

        DateTime? sessionDate = null;
        if (context.Defaults?.SessionDate is { } defaultDate)
        {
            sessionDate = defaultDate.DateTime;
        }

        return new EventSessionCreateFormState(
            context.Locations ?? new List<EventSessionCreateLocationOptionDto>(),
            context.SessionGroups ?? new List<EventSessionCreateGroupOptionDto>(),
            sessionDate,
            TryParseDefaultTime(context.Defaults?.StartTime, out var startTime) ? startTime : null,
            TryParseDefaultTime(context.Defaults?.EndTime, out var endTime) ? endTime : null);
    }

    public static EventSessionEditFormState PopulateUpdateRequest(
        UpdateEventSessionDto session,
        EventSessionDto sourceSession,
        Guid eventId,
        string eventTimeZoneId = "UTC")
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(sourceSession);

        session.Event = new UpdateEventSessionEventDto { EventId = eventId };
        session.Schedule = new UpdateEventSessionScheduleDto
        {
            StartTime = OptionalDateTimeOffset(sourceSession.StartTime),
            EndTime = OptionalDateTimeOffset(sourceSession.EndTime)
        };
        session.Location = new UpdateEventSessionLocationDto { Value = OptionalGuid(sourceSession.LocationId) };
        session.FeaturedImage = new UpdateEventSessionFeaturedImageDto { Value = OptionalGuid(sourceSession.FeaturedImageId) };
        session.Room = new UpdateEventSessionRoomDto { Value = OptionalGuid(sourceSession.RoomId) };
        session.SortOrder = new UpdateEventSessionSortOrderDto { Value = sourceSession.SortOrder.GetValueOrDefault() };
        session.Title = new UpdateEventSessionTitleDto { Value = OptionalString(sourceSession.Title ?? string.Empty) };
        session.Kind = new UpdateEventSessionKindDto { Value = OptionalInt(sourceSession.EventSessionKindId) };
        session.Description = new UpdateEventSessionDescriptionDto { Value = OptionalString(sourceSession.Description) };
        session.Slug = new UpdateEventSessionSlugDto { Value = OptionalString(sourceSession.Slug) };
        session.MaxAudienceAttendees = new UpdateEventSessionMaxAudienceAttendeesDto { Value = OptionalInt(sourceSession.MaxAudienceAttendees) };
        session.RegistrationMode = new UpdateEventSessionRegistrationModeDto { Value = OptionalInt(sourceSession.RegistrationModeId) };
        session.IslamicAspect = new UpdateEventSessionIslamicAspectUpdateDto
        {
            Value = OptionalEventSessionIslamicAspect(sourceSession.IslamicAspect)
        };

        DateTime? localStart = sourceSession.LocalStartDate.HasValue && sourceSession.LocalStartTime.HasValue
            ? sourceSession.LocalStartDate.Value.Date + sourceSession.LocalStartTime.Value
            : sourceSession.StartTime.HasValue
                ? DateTimeHelper.ConvertUtcToLocal(sourceSession.StartTime.Value, eventTimeZoneId)
                : null;
        DateTime? localEnd = sourceSession.LocalEndDate.HasValue && sourceSession.LocalEndTime.HasValue
            ? sourceSession.LocalEndDate.Value.Date + sourceSession.LocalEndTime.Value
            : sourceSession.EndTime.HasValue
                ? DateTimeHelper.ConvertUtcToLocal(sourceSession.EndTime.Value, eventTimeZoneId)
                : null;
        return new EventSessionEditFormState(
            localStart?.Date ?? DateTimeHelper.ConvertUtcToLocal(DateTimeOffset.UtcNow, eventTimeZoneId).Date,
            localStart?.TimeOfDay ?? new TimeSpan(9, 0, 0),
            localEnd?.TimeOfDay ?? new TimeSpan(10, 0, 0),
            GetPrimarySessionGroupId(sourceSession));
    }

    public static bool TryPrepareCreateRequest(
        CreateEventSessionDto session,
        Guid eventId,
        Guid? tenantId,
        DateTime? sessionDate,
        TimeSpan? startTime,
        TimeSpan? endTime,
        out string? validationError,
        string eventTimeZoneId = "UTC")
    {
        ArgumentNullException.ThrowIfNull(session);
        validationError = null;

        if (tenantId is null || tenantId == Guid.Empty)
        {
            validationError = "The event draft is missing tenant context. Return to the draft and try again.";
            return false;
        }

        if (!TryNormalizeSchedule(session.Title, sessionDate, startTime, endTime, out var title, out var start, out var end, out validationError))
        {
            return false;
        }

        if (!DateTimeHelper.TryConvertLocalToUtc(
                start,
                eventTimeZoneId,
                existingInstant: null,
                out DateTimeOffset startUtc,
                out validationError)
            || !DateTimeHelper.TryConvertLocalToUtc(
                end,
                eventTimeZoneId,
                existingInstant: null,
                out DateTimeOffset endUtc,
                out validationError))
        {
            return false;
        }

        session.EventId = eventId;
        ApplyNormalizedSchedule(session, title, startUtc, endUtc);
        return true;
    }

    public static bool TryPrepareUpdateRequest(
        UpdateEventSessionDto session,
        Guid eventId,
        Guid sessionId,
        Guid? sourceSessionId,
        DateTime? sessionDate,
        TimeSpan? startTime,
        TimeSpan? endTime,
        out string? validationError,
        string eventTimeZoneId = "UTC",
        DateTimeOffset? existingStartUtc = null,
        DateTimeOffset? existingEndUtc = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        validationError = null;

        if (sourceSessionId is null
            || sourceSessionId == Guid.Empty
            || sessionId == Guid.Empty
            || sourceSessionId != sessionId)
        {
            validationError = "The session context is invalid. Return to the event draft and try again.";
            return false;
        }

        if (!TryNormalizeSchedule(session.Title?.Value?.Value, sessionDate, startTime, endTime, out var title, out var start, out var end, out validationError))
        {
            return false;
        }

        if (!DateTimeHelper.TryConvertLocalToUtc(
                start,
                eventTimeZoneId,
                existingStartUtc,
                out DateTimeOffset startUtc,
                out validationError)
            || !DateTimeHelper.TryConvertLocalToUtc(
                end,
                eventTimeZoneId,
                existingEndUtc,
                out DateTimeOffset endUtc,
                out validationError))
        {
            return false;
        }

        session.Event ??= new UpdateEventSessionEventDto();
        session.Event.EventId = eventId;
        ApplyNormalizedSchedule(session, title, startUtc, endUtc);
        return true;
    }

    private static bool TryNormalizeSchedule(
        string? title,
        DateTime? sessionDate,
        TimeSpan? startTime,
        TimeSpan? endTime,
        out string normalizedTitle,
        out DateTime start,
        out DateTime end,
        out string? validationError)
    {
        normalizedTitle = string.Empty;
        start = default;
        end = default;
        validationError = null;

        if (string.IsNullOrWhiteSpace(title))
        {
            validationError = "Program item title is required.";
            return false;
        }

        if (!sessionDate.HasValue || !startTime.HasValue || !endTime.HasValue)
        {
            validationError = "Program item date, start time, and end time are required.";
            return false;
        }

        start = sessionDate.Value.Date + startTime.Value;
        end = sessionDate.Value.Date + endTime.Value;
        if (end <= start)
        {
            validationError = "End time must be after start time.";
            return false;
        }

        normalizedTitle = title.Trim();
        return true;
    }

    private static void ApplyNormalizedSchedule(
        CreateEventSessionDto session,
        string title,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc)
    {
        session.Title = title;
        session.MaxAudienceAttendees = session.MaxAudienceAttendees > 0 ? session.MaxAudienceAttendees : null;
        session.StartTime = startUtc;
        session.EndTime = endUtc;
    }

    private static void ApplyNormalizedSchedule(
        UpdateEventSessionDto session,
        string title,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc)
    {
        session.Title!.Value!.Value = title;
        session.MaxAudienceAttendees!.Value!.Value = session.MaxAudienceAttendees.Value.Value > 0
            ? session.MaxAudienceAttendees.Value.Value
            : null;
        session.Schedule!.StartTime!.Value = startUtc;
        session.Schedule.EndTime!.Value = endUtc;
    }

    private static bool TryParseDefaultTime(string? value, out TimeSpan time)
    {
        return TimeSpan.TryParse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            out time);
    }

    private static Guid? GetPrimarySessionGroupId(EventSessionDto session)
        => session.SessionGroups?
            .OrderByDescending(group => group.IsPrimary ?? false)
            .ThenBy(group => group.SortOrder ?? int.MaxValue)
            .Select(group => group.EventSessionGroupId)
            .FirstOrDefault(groupId => groupId.HasValue && groupId.Value != Guid.Empty);

    private static OptionalUpdateOfDateTimeOffset OptionalDateTimeOffset(DateTimeOffset? value) => new()
    {
        HasValue = true,
        Value = value
    };

    private static OptionalUpdateOfEventSessionIslamicAspectDto OptionalEventSessionIslamicAspect(
        EventSessionIslamicAspectDto? value) => new()
        {
            HasValue = true,
            Value = value
        };

    private static OptionalUpdateOfGuid OptionalGuid(Guid? value) => new()
    {
        HasValue = true,
        Value = value
    };

    private static OptionalUpdateOfint OptionalInt(int? value) => new()
    {
        HasValue = true,
        Value = value
    };

    private static OptionalUpdateOfstring OptionalString(string? value) => new()
    {
        HasValue = true,
        Value = value
    };
}

internal sealed record EventSessionCreateFormState(
    ICollection<EventSessionCreateLocationOptionDto> Locations,
    ICollection<EventSessionCreateGroupOptionDto> SessionGroups,
    DateTime? SessionDate,
    TimeSpan? StartTime,
    TimeSpan? EndTime);

internal sealed record EventSessionEditFormState(
    DateTime? SessionDate,
    TimeSpan? StartTime,
    TimeSpan? EndTime,
    Guid? PrimarySessionGroupId);
