using System.Globalization;
using Explore.Domain.Services.Scheduling;

namespace Explore.Application.Contracts.Services;

public static class EventReminderAuthorityReference
{
    private const string Prefix = "event-reminder:v2:";

    public static string Format(
        Guid sessionId,
        DateTimeOffset sessionStartUtc,
        string timeZoneId)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("SessionId is required.", nameof(sessionId));
        }

        string normalizedTimeZoneId = ScheduleTimeZoneResolver.NormalizeOrUtc(timeZoneId);
        string reference = $"{Prefix}{sessionId:N}:{sessionStartUtc.ToUniversalTime().UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture)}:{normalizedTimeZoneId}";
        return reference.Length <= 200
            ? reference
            : throw new ArgumentException("TimeZoneId is too long for reminder authority.", nameof(timeZoneId));
    }

    public static bool TryParse(
        string? value,
        out Guid sessionId,
        out DateTimeOffset sessionStartUtc,
        out string timeZoneId)
    {
        sessionId = Guid.Empty;
        sessionStartUtc = default;
        timeZoneId = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> remainder = value.AsSpan(Prefix.Length);
        int sessionSeparator = remainder.IndexOf(':');
        int ticksSeparator = sessionSeparator < 0
            ? -1
            : remainder[(sessionSeparator + 1)..].IndexOf(':');
        if (sessionSeparator != 32 || ticksSeparator < 1)
        {
            return false;
        }

        ticksSeparator += sessionSeparator + 1;
        if (!Guid.TryParseExact(remainder[..sessionSeparator], "N", out sessionId)
            || sessionId == Guid.Empty
            || !long.TryParse(
                remainder[(sessionSeparator + 1)..ticksSeparator],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long ticks)
            || ticks < DateTimeOffset.MinValue.UtcDateTime.Ticks
            || ticks > DateTimeOffset.MaxValue.UtcDateTime.Ticks
            || remainder[(ticksSeparator + 1)..] is not { IsEmpty: false } timeZoneSpan)
        {
            sessionId = Guid.Empty;
            return false;
        }

        try
        {
            timeZoneId = ScheduleTimeZoneResolver.NormalizeOrUtc(timeZoneSpan.ToString());
            if (!timeZoneSpan.SequenceEqual(timeZoneId))
            {
                sessionId = Guid.Empty;
                timeZoneId = string.Empty;
                return false;
            }

            sessionStartUtc = new DateTimeOffset(ticks, TimeSpan.Zero);
            if (!string.Equals(
                value,
                Format(sessionId, sessionStartUtc, timeZoneId),
                StringComparison.Ordinal))
            {
                sessionId = Guid.Empty;
                sessionStartUtc = default;
                timeZoneId = string.Empty;
                return false;
            }

            return true;
        }
        catch (ArgumentException)
        {
            sessionId = Guid.Empty;
            timeZoneId = string.Empty;
            return false;
        }
    }

    public static string FormatDisplay(DateTimeOffset sessionStartUtc, string timeZoneId)
    {
        TimeZoneInfo timeZone = ScheduleTimeZoneResolver.ResolveRequired(timeZoneId);
        DateTimeOffset utc = sessionStartUtc.ToUniversalTime();
        DateTimeOffset local = TimeZoneInfo.ConvertTime(utc, timeZone);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{local:yyyy-MM-dd HH:mm} [{timeZone.Id}] ({utc:yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'})");
    }
}
