using System;

namespace Explore.Domain.Services.Scheduling;

public interface IEventScheduleProjectionCalculator
{
    /// <summary>
    /// Converts the supplied UTC interval into cached local projection fields for the given IANA timezone.
    /// </summary>
    /// <param name="startUtc">Inclusive UTC start of the interval.</param>
    /// <param name="endUtc">Exclusive UTC end of the interval; must be strictly greater than <paramref name="startUtc"/>, or null for open-ended.</param>
    /// <param name="timezoneId">System timezone id (for example "Europe/Brussels"). If null/empty the calculator falls back to UTC.</param>
    LocalScheduleProjection Project(DateTimeOffset startUtc, DateTimeOffset? endUtc, string? timezoneId);
}
