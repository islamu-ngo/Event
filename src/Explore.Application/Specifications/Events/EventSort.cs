using System.Linq.Expressions;
using Explore.Domain;

namespace Explore.Application.Specifications.Events;

/// <summary>
/// Static factory for creating event sort specifications.
/// Each sort translates to a database-level ORDER BY clause via EF Core expression trees.
/// </summary>
public sealed class EventSort : ISortSpecification<Event>
{
    public Expression<Func<Event, object>> KeySelector { get; }

    private EventSort(Expression<Func<Event, object>> keySelector) => KeySelector = keySelector;

    /// <summary>
    /// Sort by first session date.
    /// </summary>
    public static EventSort Date { get; } = new(e => e.FirstSessionDate!);

    /// <summary>
    /// Sort by event title.
    /// </summary>
    public static EventSort Title { get; } = new(e => e.DiscoveryTitleSortKey);

    /// <summary>
    /// Sort by total views (popularity).
    /// </summary>
    public static EventSort Views { get; } = new(e => e.TotalViews);

    /// <summary>
    /// Sort by creation date.
    /// </summary>
    public static EventSort CreatedAt { get; } = new(e => e.CreatedAt);

    /// <summary>
    /// Temporal sort sentinel — not-past events first, then past.
    /// Handled specially in EventQuerySpecification; KeySelector is not used.
    /// </summary>
    public static readonly EventSort Temporal = new(e => e.FirstSessionDate!);
}
