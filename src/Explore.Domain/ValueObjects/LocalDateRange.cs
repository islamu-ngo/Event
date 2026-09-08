using System.Globalization;

namespace Explore.Domain.ValueObjects;

public sealed record LocalDateRange
{
    private LocalDateRange(DateOnly start, DateOnly end)
    {
        Start = start;
        End = end;
    }

    public DateOnly Start { get; }
    public DateOnly End { get; }

    public static LocalDateRange Create(DateOnly start, DateOnly end)
    {
        if (start > end)
        {
            throw new ArgumentOutOfRangeException(nameof(end), "End date must not precede start date.");
        }

        return new LocalDateRange(start, end);
    }

    public bool Contains(DateOnly date) => date >= Start && date <= End;

    public bool Overlaps(LocalDateRange other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Start <= other.End && other.Start <= End;
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Start:yyyy-MM-dd}/{End:yyyy-MM-dd}");
}
