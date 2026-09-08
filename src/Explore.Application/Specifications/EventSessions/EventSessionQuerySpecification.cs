using System.Linq.Expressions;
using Explore.Domain;

namespace Explore.Application.Specifications.EventSessions;

/// <summary>
/// Immutable builder for composing event session query filters and sorting.
/// Supports projection-backed custom property filters (Layer 3) and basic sorting.
/// </summary>
/// <remarks>
/// This specification is intentionally leaner than <see cref="Events.EventQuerySpecification"/>
/// because sessions do not currently have junction-table or aspect-filter infrastructure.
/// It can be extended with direct filters and subquery filters as session filtering needs grow.
/// </remarks>
public sealed class EventSessionQuerySpecification
{
    private readonly List<EventSessionCustomPropertyProjectionFilter> _projectionFilters;
    private readonly Expression<Func<EventSession, object>>? _sortKeySelector;
    private readonly bool _sortDescending;

    public EventSessionQuerySpecification()
    {
        _projectionFilters = [];
        _sortKeySelector = null;
        _sortDescending = false;
    }

    private EventSessionQuerySpecification(
        List<EventSessionCustomPropertyProjectionFilter> projectionFilters,
        Expression<Func<EventSession, object>>? sortKeySelector,
        bool sortDescending)
    {
        _projectionFilters = projectionFilters;
        _sortKeySelector = sortKeySelector;
        _sortDescending = sortDescending;
    }

    public IReadOnlyList<EventSessionCustomPropertyProjectionFilter> ProjectionFilters =>
        _projectionFilters.AsReadOnly();

    public Expression<Func<EventSession, object>>? SortKeySelector => _sortKeySelector;

    public bool SortDescending => _sortDescending;

    public bool HasFilters => _projectionFilters.Count > 0;

    public bool HasSort => _sortKeySelector is not null;

    public EventSessionQuerySpecification And(EventSessionCustomPropertyProjectionFilter filter) =>
        new([.. _projectionFilters, filter], _sortKeySelector, _sortDescending);

    public EventSessionQuerySpecification SortBy(Expression<Func<EventSession, object>> keySelector) =>
        new([.. _projectionFilters], keySelector, false);

    public EventSessionQuerySpecification SortByDescending(Expression<Func<EventSession, object>> keySelector) =>
        new([.. _projectionFilters], keySelector, true);

    public IQueryable<EventSession> Apply(IQueryable<EventSession> query)
    {
        if (_sortKeySelector is not null)
        {
            query = _sortDescending
                ? query.OrderByDescending(_sortKeySelector)
                : query.OrderBy(_sortKeySelector);
        }

        return query;
    }

    public string ToCacheKeySuffix()
    {
        var parts = new List<string>();

        foreach (var projFilter in _projectionFilters)
        {
            parts.Add($"pf:{projFilter.FilterType}:{projFilter.Value}");
        }

        if (_sortKeySelector is not null)
        {
            var direction = _sortDescending ? "desc" : "asc";
            parts.Add($"s:{_sortKeySelector.Body}:{direction}");
        }

        return parts.Count > 0 ? string.Join("|", parts) : "none";
    }
}
