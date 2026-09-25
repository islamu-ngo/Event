using System.Linq.Expressions;
using Explore.Domain;

namespace Explore.Application.Specifications.EventResources;

public sealed class EventResourceFilter : IFilterSpecification<EventResource>
{
    private EventResourceFilter(Expression<Func<EventResource, bool>> predicate) => Predicate = predicate;

    public Expression<Func<EventResource, bool>> Predicate { get; }

    public static EventResourceFilter PublicDisclosure() => new(resource =>
        resource.DisclosureModeId == (int)Explore.Domain.Enums.EventResourceDisclosureModeEnum.Teaser
        || resource.DisclosureModeId == (int)Explore.Domain.Enums.EventResourceDisclosureModeEnum.Public
        || resource.AudienceRules.Any(rule => rule.AudienceKindId == (int)Explore.Domain.Enums.EventResourceAudienceKindEnum.Public));

    public static EventResourceFilter Event(Guid eventId) => new(resource => resource.EventId == eventId);
    public static EventResourceFilter Session(Guid eventSessionId) => new(resource => resource.EventSessionId == eventSessionId);
    public static EventResourceFilter PublicationState(int publicationStateId) => new(resource => resource.PublicationStateId == publicationStateId);
    public static EventResourceFilter After(EventResourceCursor cursor) => new(resource =>
        resource.SortOrder > cursor.SortOrder
        || resource.SortOrder == cursor.SortOrder && resource.Id.CompareTo(cursor.Id) > 0);
}

public sealed record EventResourceCursor(int SortOrder, Guid Id);
