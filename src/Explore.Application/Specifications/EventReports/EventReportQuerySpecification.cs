using Explore.Domain;

namespace Explore.Application.Specifications.EventReports;

public sealed class EventReportQuerySpecification : IQuerySpecification<EventReport>
{
    private readonly List<IFilterSpecification<EventReport>> _filters;
    private readonly ISortSpecification<EventReport>? _sort;
    private readonly bool _sortDescending;

    public EventReportQuerySpecification()
    {
        _filters = [];
        _sort = null;
        _sortDescending = false;
    }

    private EventReportQuerySpecification(
        List<IFilterSpecification<EventReport>> filters,
        ISortSpecification<EventReport>? sort,
        bool sortDescending)
    {
        _filters = filters;
        _sort = sort;
        _sortDescending = sortDescending;
    }

    public IReadOnlyList<IFilterSpecification<EventReport>> Filters => _filters.AsReadOnly();

    public ISortSpecification<EventReport>? Sort => _sort;

    public bool SortDescending => _sortDescending;

    public bool HasFilters => _filters.Count > 0;

    public bool HasSort => _sort is not null;

    public EventReportQuerySpecification And(EventReportFilter filter) =>
        new([.. _filters, filter], _sort, _sortDescending);

    IQuerySpecification<EventReport> IQuerySpecification<EventReport>.And(IFilterSpecification<EventReport> filter) =>
        new EventReportQuerySpecification([.. _filters, filter], _sort, _sortDescending);

    public EventReportQuerySpecification SortBy(EventReportSort sort) =>
        new([.. _filters], sort, false);

    public EventReportQuerySpecification SortByDescending(EventReportSort sort) =>
        new([.. _filters], sort, true);

    IQuerySpecification<EventReport> IQuerySpecification<EventReport>.SortBy(ISortSpecification<EventReport> sort) =>
        new EventReportQuerySpecification([.. _filters], sort, false);

    IQuerySpecification<EventReport> IQuerySpecification<EventReport>.SortByDescending(ISortSpecification<EventReport> sort) =>
        new EventReportQuerySpecification([.. _filters], sort, true);

    public IQueryable<EventReport> Apply(IQueryable<EventReport> query)
    {
        foreach (var filter in _filters)
        {
            query = query.Where(filter.Predicate);
        }

        if (_sort is not null)
        {
            query = _sortDescending
                ? query.OrderByDescending(_sort.KeySelector)
                : query.OrderBy(_sort.KeySelector);
        }

        return query;
    }
}
