using System.Linq.Expressions;
using Explore.Domain;

namespace Explore.Application.Specifications.EventResources;

public sealed class EventResourceSort : ISortSpecification<EventResource>
{
    private EventResourceSort(Expression<Func<EventResource, object>> keySelector) => KeySelector = keySelector;

    public Expression<Func<EventResource, object>> KeySelector { get; }

    public static EventResourceSort SortOrder { get; } = new(resource => resource.SortOrder);
    public static EventResourceSort Title { get; } = new(resource => resource.Title);
    public static EventResourceSort CreatedAt { get; } = new(resource => resource.CreatedAt);
}
