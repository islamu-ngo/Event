using Explore.Domain.Enums;

namespace Explore.Domain.ValueObjects;

public sealed record EventResourceAvailability
{
    public DateTimeOffset? AbsoluteStartUtc { get; private init; }
    public DateTimeOffset? AbsoluteEndUtc { get; private init; }
    public EventResourceAvailabilityAnchorEnum? StartAnchor { get; private init; }
    public long? StartOffsetTicks { get; private init; }
    public EventResourceAvailabilityAnchorEnum? EndAnchor { get; private init; }
    public long? EndOffsetTicks { get; private init; }

    private EventResourceAvailability() { }

    public static EventResourceAvailability Create(
        DateTimeOffset? absoluteStartUtc = null,
        DateTimeOffset? absoluteEndUtc = null,
        EventResourceAvailabilityAnchorEnum? startAnchor = null,
        TimeSpan? startOffset = null,
        EventResourceAvailabilityAnchorEnum? endAnchor = null,
        TimeSpan? endOffset = null)
    {
        ValidateBoundary(absoluteStartUtc, startAnchor, startOffset);
        ValidateBoundary(absoluteEndUtc, endAnchor, endOffset);
        if (absoluteStartUtc.HasValue && absoluteEndUtc <= absoluteStartUtc
            || startAnchor.HasValue && startAnchor == endAnchor && endOffset <= startOffset)
        {
            throw new ArgumentException("Availability end must follow its start.");
        }

        return new EventResourceAvailability
        {
            AbsoluteStartUtc = absoluteStartUtc?.ToUniversalTime(),
            AbsoluteEndUtc = absoluteEndUtc?.ToUniversalTime(),
            StartAnchor = startAnchor,
            StartOffsetTicks = startOffset?.Ticks,
            EndAnchor = endAnchor,
            EndOffsetTicks = endOffset?.Ticks
        };
    }

    public bool IsAvailableAt(DateTimeOffset nowUtc, EventResourceScheduleFacts schedule) =>
        TryResolve(schedule, out var start, out var end)
        && (!start.HasValue || nowUtc >= start)
        && (!end.HasValue || nowUtc < end);

    public bool TryResolve(EventResourceScheduleFacts schedule, out DateTimeOffset? start, out DateTimeOffset? end)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        start = null;
        end = null;
        return TryResolveBoundary(AbsoluteStartUtc, StartAnchor, StartOffsetTicks, schedule, out start)
            && TryResolveBoundary(AbsoluteEndUtc, EndAnchor, EndOffsetTicks, schedule, out end)
            && (!start.HasValue || !end.HasValue || end > start);
    }

    private static void ValidateBoundary(DateTimeOffset? absolute,
        EventResourceAvailabilityAnchorEnum? anchor, TimeSpan? offset)
    {
        if (absolute.HasValue && anchor.HasValue || anchor.HasValue != offset.HasValue
            || anchor.HasValue && !Enum.IsDefined(anchor.Value))
        {
            throw new ArgumentException("Availability requires an absolute instant or a known anchor and offset.");
        }
    }

    private static bool TryResolveBoundary(DateTimeOffset? absolute,
        EventResourceAvailabilityAnchorEnum? anchor, long? offsetTicks,
        EventResourceScheduleFacts schedule, out DateTimeOffset? instant)
    {
        instant = absolute;
        if (!anchor.HasValue)
        {
            return !offsetTicks.HasValue;
        }

        if (absolute.HasValue || !offsetTicks.HasValue)
        {
            return false;
        }

        var anchorInstant = anchor switch
        {
            EventResourceAvailabilityAnchorEnum.EventStart => schedule.EventStartUtc,
            EventResourceAvailabilityAnchorEnum.EventEnd => schedule.EventEndUtc,
            EventResourceAvailabilityAnchorEnum.SessionStart => schedule.SessionStartUtc,
            EventResourceAvailabilityAnchorEnum.SessionEnd => schedule.SessionEndUtc,
            _ => null
        };
        if (!anchorInstant.HasValue)
        {
            return false;
        }

        try
        {
            instant = anchorInstant.Value.AddTicks(offsetTicks.Value).ToUniversalTime();
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}

public sealed record EventResourceScheduleFacts(
    DateTimeOffset? EventStartUtc,
    DateTimeOffset? EventEndUtc,
    DateTimeOffset? SessionStartUtc,
    DateTimeOffset? SessionEndUtc);
