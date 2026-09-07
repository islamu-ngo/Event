using System;

namespace Explore.Domain.Services.Scheduling;

public static class ScheduleTimeZoneResolver
{
    public const string UtcId = "UTC";

    public static string NormalizeOrUtc(string? timezoneId)
    {
        return ResolveOrUtc(timezoneId).Id;
    }

    public static bool IsValidOrBlank(string? timezoneId)
    {
        if (string.IsNullOrWhiteSpace(timezoneId))
        {
            return true;
        }

        try
        {
            _ = ResolveRequired(timezoneId);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static TimeZoneInfo ResolveOrUtc(string? timezoneId)
    {
        return string.IsNullOrWhiteSpace(timezoneId)
            ? TimeZoneInfo.Utc
            : ResolveRequired(timezoneId);
    }

    public static TimeZoneInfo ResolveRequired(string timezoneId)
    {
        var normalizedId = timezoneId.Trim();
        if (normalizedId.Length == 0)
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(normalizedId);
        }
        catch (TimeZoneNotFoundException ex)
        {
            throw new ArgumentException("TimezoneId must be a valid system timezone identifier.", nameof(timezoneId), ex);
        }
        catch (InvalidTimeZoneException ex)
        {
            throw new ArgumentException("TimezoneId must be a valid system timezone identifier.", nameof(timezoneId), ex);
        }
    }
}
