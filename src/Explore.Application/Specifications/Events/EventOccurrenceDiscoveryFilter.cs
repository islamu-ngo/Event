using System.Collections.Immutable;

namespace Explore.Application.Specifications.Events;

/// <summary>
/// One public occurrence must satisfy every constraint. Location IDs are resolved
/// by the server's governed area authority, never accepted as anonymous venue input.
/// A null location set is unrestricted; an empty set intentionally matches nothing.
/// </summary>
public sealed record EventOccurrenceDiscoveryFilter
{
    public EventOccurrenceDiscoveryFilter(
        DateOnly? dateFrom,
        DateOnly? dateTo,
        TemporalView view,
        DateTimeOffset now,
        IReadOnlyList<Guid>? locationIds = null)
    {
        if (dateFrom > dateTo)
        {
            throw new ArgumentException("The occurrence date window must be ordered.", nameof(dateTo));
        }

        if (!Enum.IsDefined(view))
        {
            throw new ArgumentOutOfRangeException(nameof(view));
        }

        DateFrom = dateFrom;
        DateTo = dateTo;
        View = view;
        Now = now.ToUniversalTime();
        LocationIds = locationIds?.Distinct().Order().ToImmutableArray();
    }

    public DateOnly? DateFrom { get; }
    public DateOnly? DateTo { get; }
    public TemporalView View { get; }
    public DateTimeOffset Now { get; }
    public IReadOnlyList<Guid>? LocationIds { get; }

    public override string ToString() =>
        $"{DateFrom:yyyy-MM-dd}:{DateTo:yyyy-MM-dd}:{View}:{Now:O}:{(LocationIds is null ? "*" : string.Join(',', LocationIds))}";
}
