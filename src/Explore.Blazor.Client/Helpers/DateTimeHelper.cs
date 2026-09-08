namespace Explore.Blazor.Client.Helpers;

public static class DateTimeHelper
{
    public static bool TryConvertLocalToUtc(
        DateTime localDateTime,
        string? timeZoneId,
        DateTimeOffset? existingInstant,
        out DateTimeOffset utc,
        out string? validationError)
    {
        utc = default;
        validationError = null;
        if (!TryResolveTimeZone(timeZoneId, out TimeZoneInfo timeZone, out validationError))
        {
            return false;
        }

        DateTime wallTime = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(wallTime))
        {
            validationError = $"{wallTime:yyyy-MM-dd HH:mm} does not exist in {timeZone.Id} because the clock moves forward. Choose another time.";
            return false;
        }

        TimeSpan offset;
        if (timeZone.IsAmbiguousTime(wallTime))
        {
            DateTimeOffset? existingLocal = existingInstant.HasValue
                ? TimeZoneInfo.ConvertTime(existingInstant.Value.ToUniversalTime(), timeZone)
                : null;
            if (!existingLocal.HasValue || existingLocal.Value.DateTime != wallTime)
            {
                validationError = $"{wallTime:yyyy-MM-dd HH:mm} occurs twice in {timeZone.Id}. Choose a time outside the repeated hour.";
                return false;
            }

            offset = existingLocal.Value.Offset;
            if (!timeZone.GetAmbiguousTimeOffsets(wallTime).Contains(offset))
            {
                validationError = $"The saved occurrence of {wallTime:yyyy-MM-dd HH:mm} is no longer valid in {timeZone.Id}. Choose another time.";
                return false;
            }
        }
        else
        {
            offset = timeZone.GetUtcOffset(wallTime);
        }

        utc = new DateTimeOffset(wallTime, offset).ToUniversalTime();
        return true;
    }

    public static bool TryCombineDateTimeToUtc(
        DateTime? date,
        TimeSpan? time,
        string? timeZoneId,
        DateTimeOffset? existingInstant,
        out DateTimeOffset utc,
        out string? validationError)
    {
        utc = default;
        validationError = null;
        if (!date.HasValue || !time.HasValue)
        {
            validationError = "Choose a date and time.";
            return false;
        }

        return TryConvertLocalToUtc(
            date.Value.Date + time.Value,
            timeZoneId,
            existingInstant,
            out utc,
            out validationError);
    }

    public static DateTime ConvertUtcToLocal(DateTimeOffset utcDateTimeOffset, string timeZoneId)
    {
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        return TimeZoneInfo.ConvertTime(utcDateTimeOffset.ToUniversalTime(), timeZone).DateTime;
    }

    private static bool TryResolveTimeZone(
        string? timeZoneId,
        out TimeZoneInfo timeZone,
        out string? validationError)
    {
        timeZone = TimeZoneInfo.Utc;
        validationError = null;
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            validationError = "The event timezone is missing. Set it before entering program times.";
            return false;
        }

        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            validationError = $"The event timezone '{timeZoneId}' is not available on this system.";
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            validationError = $"The event timezone '{timeZoneId}' is invalid.";
            return false;
        }
    }
}
