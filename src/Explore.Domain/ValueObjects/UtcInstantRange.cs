using System.Globalization;

namespace Explore.Domain.ValueObjects;

public sealed record UtcInstantRange
{
    private UtcInstantRange(DateTimeOffset start, DateTimeOffset end)
    {
        Start = start;
        End = end;
    }

    public DateTimeOffset Start { get; }
    public DateTimeOffset End { get; }

    public static UtcInstantRange Create(DateTimeOffset start, DateTimeOffset end)
    {
        DateTimeOffset normalizedStart = start.ToUniversalTime();
        DateTimeOffset normalizedEnd = end.ToUniversalTime();
        if (normalizedEnd <= normalizedStart)
        {
            throw new ArgumentOutOfRangeException(nameof(end), "End instant must follow start instant.");
        }

        return new UtcInstantRange(normalizedStart, normalizedEnd);
    }

    public bool Contains(DateTimeOffset instant)
    {
        DateTimeOffset normalized = instant.ToUniversalTime();
        return normalized >= Start && normalized < End;
    }

    public bool Overlaps(UtcInstantRange other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Start < other.End && other.Start < End;
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Start:O}/{End:O}");
}
