using Explore.Domain;

namespace Explore.Application.Specifications.Events;

/// <summary>
/// Fluent builder for composing event query filters and sorting.
/// Follows the specification pattern with immutable builder semantics.
/// Supports core event filters, aspect-specific filters (Islamic/Tech),
/// subquery filters (junction tables), and sorting.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// var spec = new EventQuerySpecification()
///     .And(EventFilter.SearchTerm("workshop"))            // core entity filter
///     .And(EventFilter.Format(formatId))                  // core entity filter
///     .And(EventSubqueryFilter.Category(categoryId))      // junction table subquery
///     .And(IslamicAspectFilter.GenderMode(mode))          // aspect filter (module-conditional)
///     .And(TechAspectFilter.SkillLevel(SkillLevel.Advanced)) // aspect filter (module-conditional)
///     .And(AspectPresenceFilter.HasIslamicAspect())        // aspect presence filter
///     .SortByDescending(EventSort.Date);                   // sort
///
/// // In repository:
/// var query = spec.Apply(dbContext.Events.AsQueryable(), dbContext);
/// </code>
/// </remarks>
public sealed class EventQuerySpecification : IQuerySpecification<Event>
{
    private readonly List<IFilterSpecification<Event>> _filters;
    private readonly List<EventSubqueryFilter> _subqueryFilters;
    private readonly List<EventCustomPropertyProjectionFilter> _projectionFilters;
    private readonly ISortSpecification<Event>? _sort;
    private readonly bool _sortDescending;

    /// <summary>
    /// Creates a new empty query specification.
    /// </summary>
    public EventQuerySpecification()
    {
        _filters = [];
        _subqueryFilters = [];
        _projectionFilters = [];
        _sort = null;
        _sortDescending = false;
    }

    private EventQuerySpecification(
        List<IFilterSpecification<Event>> filters,
        List<EventSubqueryFilter> subqueryFilters,
        List<EventCustomPropertyProjectionFilter> projectionFilters,
        ISortSpecification<Event>? sort,
        bool sortDescending)
    {
        _filters = filters;
        _subqueryFilters = subqueryFilters;
        _projectionFilters = projectionFilters;
        _sort = sort;
        _sortDescending = sortDescending;
    }

    /// <inheritdoc />
    public IReadOnlyList<IFilterSpecification<Event>> Filters => _filters.AsReadOnly();

    /// <summary>
    /// Gets the subquery filters that require DbContext access.
    /// </summary>
    public IReadOnlyList<EventSubqueryFilter> SubqueryFilters => _subqueryFilters.AsReadOnly();

    public EventOccurrenceDiscoveryFilter? Occurrence => _subqueryFilters
        .Where(filter => filter.FilterType == EventSubqueryFilterType.Occurrence)
        .Select(filter => (EventOccurrenceDiscoveryFilter)filter.Value)
        .SingleOrDefault();

    /// <summary>Replaces the occurrence window without mutating the existing specification.</summary>
    public EventQuerySpecification WithOccurrence(EventOccurrenceDiscoveryFilter filter) =>
        new([.. _filters],
            [.. _subqueryFilters.Where(item => item.FilterType != EventSubqueryFilterType.Occurrence),
                EventSubqueryFilter.Occurrence(filter)],
            [.. _projectionFilters], _sort, _sortDescending);

    /// <summary>
    /// Gets the projection filters for custom property discovery (Layer 3).
    /// Applied at the repository level via correlated subqueries against projection tables.
    /// </summary>
    public IReadOnlyList<EventCustomPropertyProjectionFilter> ProjectionFilters => _projectionFilters.AsReadOnly();

    /// <inheritdoc />
    public ISortSpecification<Event>? Sort => _sort;

    /// <inheritdoc />
    public bool SortDescending => _sortDescending;

    /// <inheritdoc />
    public bool HasFilters => _filters.Count > 0 || _subqueryFilters.Count > 0 || _projectionFilters.Count > 0;

    /// <inheritdoc />
    public bool HasSort => _sort is not null;

    /// <summary>
    /// Adds a direct filter specification (AND composition).
    /// Returns a new specification instance (immutable builder).
    /// </summary>
    public EventQuerySpecification And(EventFilter filter) =>
        new([.. _filters, filter], [.. _subqueryFilters], [.. _projectionFilters], _sort, _sortDescending);

    /// <summary>
    /// Adds an Islamic aspect filter (AND composition).
    /// Only compose when the Islamic module is enabled for the current tenant.
    /// </summary>
    public EventQuerySpecification And(IslamicAspectFilter filter) =>
        new([.. _filters, filter], [.. _subqueryFilters], [.. _projectionFilters], _sort, _sortDescending);

    /// <summary>
    /// Adds a Tech aspect filter (AND composition).
    /// Only compose when the Tech module is enabled for the current tenant.
    /// </summary>
    public EventQuerySpecification And(TechAspectFilter filter) =>
        new([.. _filters, filter], [.. _subqueryFilters], [.. _projectionFilters], _sort, _sortDescending);

    /// <summary>
    /// Adds an aspect presence filter (AND composition).
    /// Filters events by whether they have specific aspects configured.
    /// </summary>
    public EventQuerySpecification And(AspectPresenceFilter filter) =>
        new([.. _filters, filter], [.. _subqueryFilters], [.. _projectionFilters], _sort, _sortDescending);

    /// <summary>
    /// Adds a subquery filter that requires DbContext access (AND composition).
    /// Includes junction table filters.
    /// Returns a new specification instance (immutable builder).
    /// </summary>
    public EventQuerySpecification And(EventSubqueryFilter filter) =>
        new([.. _filters], [.. _subqueryFilters, filter], [.. _projectionFilters], _sort, _sortDescending);

    /// <summary>
    /// Adds a projection filter for custom property discovery (AND composition).
    /// Only compose when <c>custom_properties.projection_discovery_enabled</c> is true for the tenant.
    /// </summary>
    public EventQuerySpecification And(EventCustomPropertyProjectionFilter filter) =>
        new([.. _filters], [.. _subqueryFilters], [.. _projectionFilters, filter], _sort, _sortDescending);

    /// <inheritdoc />
    IQuerySpecification<Event> IQuerySpecification<Event>.And(IFilterSpecification<Event> filter) =>
        new EventQuerySpecification([.. _filters, filter], [.. _subqueryFilters], [.. _projectionFilters], _sort, _sortDescending);

    /// <inheritdoc />
    public EventQuerySpecification SortBy(EventSort sort) =>
        new([.. _filters], [.. _subqueryFilters], [.. _projectionFilters], sort, false);

    /// <inheritdoc />
    public EventQuerySpecification SortByDescending(EventSort sort) =>
        new([.. _filters], [.. _subqueryFilters], [.. _projectionFilters], sort, true);

    /// <inheritdoc />
    IQuerySpecification<Event> IQuerySpecification<Event>.SortBy(ISortSpecification<Event> sort) =>
        new EventQuerySpecification([.. _filters], [.. _subqueryFilters], [.. _projectionFilters], sort, false);

    /// <inheritdoc />
    IQuerySpecification<Event> IQuerySpecification<Event>.SortByDescending(ISortSpecification<Event> sort) =>
        new EventQuerySpecification([.. _filters], [.. _subqueryFilters], [.. _projectionFilters], sort, true);

    /// <summary>
    /// Applies all direct filter predicates and sorting to the given queryable.
    /// Note: Subquery filters must be applied separately at the repository level
    /// using ApplySubqueryFilters where the DbContext is available.
    /// </summary>
    public IQueryable<Event> Apply(IQueryable<Event> query) => Apply(query, null);

    /// <summary>
    /// Applies all direct filter predicates and sorting to the given queryable with temporal context for bucketed sorting.
    /// </summary>
    public IQueryable<Event> Apply(IQueryable<Event> query, DateTimeOffset? now)
    {
        // Apply direct filters
        foreach (var filter in _filters)
        {
            query = query.Where(filter.Predicate);
        }

        // Apply sorting
        if (_sort is not null)
        {
            if (_sort == EventSort.Temporal && now.HasValue)
            {
                var ts = now.Value;
                // Bucket 1: Not Past (LastSessionStartUtc > now) -> Sort by NextSessionStartUtc ASC
                // Bucket 2: Past (LastSessionStartUtc <= now) -> Sort by LastSessionStartUtc DESC
                // We use LastSessionStartUtc <= now as the first sort key (false < true, so NotPast comes first)
                query = query.OrderBy(e => e.LastSessionStartUtc != null && e.LastSessionStartUtc <= ts)
                             .ThenBy(e => e.LastSessionStartUtc != null && e.LastSessionStartUtc <= ts
                                 ? -e.LastSessionStartUtc.Value.Ticks
                                 : e.FirstSessionStartUtc.Value.Ticks);
            }
            else
            {
                query = _sortDescending
                    ? query.OrderByDescending(_sort.KeySelector)
                    : query.OrderBy(_sort.KeySelector);
            }
        }

        return query;
    }

    /// <summary>
    /// Generates a stable cache key suffix representing all active filters and sort.
    /// Used for differentiated caching of filtered results.
    /// </summary>
    public string ToCacheKeySuffix()
    {
        var parts = new List<string>();

        foreach (var filter in _filters)
        {
            // Use the filter's predicate body hash for uniqueness
            parts.Add($"f:{filter.Predicate.Body}");
        }

        foreach (var subFilter in _subqueryFilters)
        {
            parts.Add($"sq:{subFilter.FilterType}:{subFilter.Value}");
        }

        foreach (var projFilter in _projectionFilters)
        {
            parts.Add($"pf:{projFilter.FilterType}:{projFilter.Value}");
        }

        if (_sort is not null)
        {
            var direction = _sortDescending ? "desc" : "asc";
            parts.Add($"s:{_sort.KeySelector.Body}:{direction}");
        }

        return parts.Count > 0 ? string.Join("|", parts) : "none";
    }
}
