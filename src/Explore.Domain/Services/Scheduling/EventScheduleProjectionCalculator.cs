using System;

namespace Explore.Domain.Services.Scheduling;

public sealed class EventScheduleProjectionCalculator : IEventScheduleProjectionCalculator
{
    public LocalScheduleProjection Project(DateTimeOffset startUtc, DateTimeOffset? endUtc, string? timezoneId)
    {
        if (endUtc.HasValue && endUtc.Value <= startUtc)
        {
            throw new ArgumentException("endUtc must be strictly greater than startUtc.", nameof(endUtc));
        }

        var timezone = ScheduleTimeZoneResolver.ResolveOrUtc(timezoneId);

        var localStart = TimeZoneInfo.ConvertTime(startUtc, timezone);

        var startDate = DateOnly.FromDateTime(localStart.DateTime);
        var startTime = TimeOnly.FromDateTime(localStart.DateTime);
        var startMinute = (startTime.Hour * 60) + startTime.Minute;

        DateOnly? endDate = null;
        TimeOnly? endTime = null;
        int? endMinute = null;

        if (endUtc.HasValue)
        {
            var localEnd = TimeZoneInfo.ConvertTime(endUtc.Value, timezone);
            endDate = DateOnly.FromDateTime(localEnd.DateTime);
            endTime = TimeOnly.FromDateTime(localEnd.DateTime);
            endMinute = (endTime.Value.Hour * 60) + endTime.Value.Minute;
        }

        return new LocalScheduleProjection(
            startDate,
            endDate,
            startTime,
            endTime,
            startMinute,
            endMinute);
    }

}
