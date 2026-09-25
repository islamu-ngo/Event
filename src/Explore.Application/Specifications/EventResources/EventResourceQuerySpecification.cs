using Explore.Domain;

namespace Explore.Application.Specifications.EventResources;

public sealed class EventResourceQuerySpecification : IQuerySpecification<EventResource>
{
    private readonly IReadOnlyList<IFilterSpecification<EventResource>> _filters;

    public EventResourceQuerySpecification()
        : this([], null, false)
    {
    }

    private EventResourceQuerySpecification(
        IReadOnlyList<IFilterSpecification<EventResource>> filters,
        ISortSpecification<EventResource>? sort,
        bool sortDescending)
    {
        _filters = Array.AsReadOnly(filters.ToArray());
        Sort = sort;
        SortDescending = sortDescending;
    }

    public IReadOnlyList<IFilterSpecification<EventResource>> Filters => _filters;
    public ISortSpecification<EventResource>? Sort { get; }
    public bool SortDescending { get; }
    public bool HasFilters => _filters.Count > 0;
    public bool HasSort => Sort is not null;

    public EventResourceQuerySpecification And(EventResourceFilter filter) =>
        new([.. _filters, filter], Sort, SortDescending);

    IQuerySpecification<EventResource> IQuerySpecification<EventResource>.And(IFilterSpecification<EventResource> filter) =>
        new EventResourceQuerySpecification([.. _filters, filter], Sort, SortDescending);

    public EventResourceQuerySpecification SortBy(EventResourceSort sort) => new(_filters, sort, false);
    public EventResourceQuerySpecification SortByDescending(EventResourceSort sort) => new(_filters, sort, true);

    IQuerySpecification<EventResource> IQuerySpecification<EventResource>.SortBy(ISortSpecification<EventResource> sort) =>
        new EventResourceQuerySpecification(_filters, sort, false);

    IQuerySpecification<EventResource> IQuerySpecification<EventResource>.SortByDescending(ISortSpecification<EventResource> sort) =>
        new EventResourceQuerySpecification(_filters, sort, true);

    public IQueryable<EventResource> Apply(IQueryable<EventResource> query)
    {
        foreach (IFilterSpecification<EventResource> filter in _filters)
        {
            query = query.Where(filter.Predicate);
        }
        if (Sort is not null)
        {
            query = SortDescending ? query.OrderByDescending(Sort.KeySelector) : query.OrderBy(Sort.KeySelector);
        }
        return query;
    }
}
