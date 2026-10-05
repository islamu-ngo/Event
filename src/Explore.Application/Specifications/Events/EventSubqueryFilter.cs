using Explore.Application.Features.Events.Requests.Queries;
using Explore.Domain;

namespace Explore.Application.Specifications.Events;

/// <summary>
/// Temporal view filter for event listing queries.
/// </summary>
public enum TemporalView
{
    /// <summary>Events where the first session has not started yet.</summary>
    Upcoming,

    /// <summary>Events currently in progress (first session started, last not yet ended).</summary>
    Ongoing,

    /// <summary>Events where the last session has already ended.</summary>
    Past,

    /// <summary>Events that are either upcoming or ongoing (default filter).</summary>
    UpcomingAndOngoing,

    /// <summary>No temporal filter — returns all events.</summary>
    All
}

/// <summary>
/// Represents a filter that requires access to related DbSets (junction tables)
/// and cannot be expressed as a simple <see cref="IFilterSpecification{T}"/> predicate.
/// These filters are applied at the repository level where the DbContext is available.
/// </summary>
/// <remarks>
/// Category, Tag, Location, Language, and RegistrationMode filters require subqueries
/// against junction tables (EventCategories, EventTags, EventSessions, EventSessionLanguages)
/// that are not navigable from the Event entity directly.
/// </remarks>
public sealed class EventSubqueryFilter
{
    /// <summary>
    /// Gets the type of subquery filter.
    /// </summary>
    public EventSubqueryFilterType FilterType { get; }

    /// <summary>
    /// Gets the filter value (Guid for Category/Location, List&lt;Guid&gt; for tag filters, int for Language/RegistrationMode).
    /// </summary>
    public object Value { get; }

    private EventSubqueryFilter(EventSubqueryFilterType filterType, object value)
    {
        FilterType = filterType;
        Value = value;
    }

    /// <summary>
    /// Filters events that have a specific category assigned (via EventCategories junction table).
    /// </summary>
    public static EventSubqueryFilter Category(Guid categoryId) =>
        new(EventSubqueryFilterType.Category, categoryId);

    /// <summary>
    /// Filters events that have ALL specified categories assigned (AND logic).
    /// Each category generates a correlated EXISTS subquery against EventCategories.
    /// </summary>
    public static EventSubqueryFilter CategoriesIncludedAll(List<Guid> categoryIds) =>
        new(EventSubqueryFilterType.CategoriesIncludedAll, categoryIds);

    /// <summary>
    /// Filters events that have at least one of the specified categories assigned (OR logic).
    /// Uses a single IN clause against EventCategories.
    /// </summary>
    public static EventSubqueryFilter CategoriesIncludedAny(List<Guid> categoryIds) =>
        new(EventSubqueryFilterType.CategoriesIncludedAny, categoryIds);

    /// <summary>
    /// Excludes events that have ANY of the specified categories (OR logic).
    /// Events with any excluded category are filtered out.
    /// </summary>
    public static EventSubqueryFilter CategoriesExcludedAny(List<Guid> categoryIds) =>
        new(EventSubqueryFilterType.CategoriesExcludedAny, categoryIds);

    /// <summary>
    /// Excludes events only if they have ALL of the specified categories simultaneously (AND logic).
    /// Events are excluded only when every excluded category is present.
    /// </summary>
    public static EventSubqueryFilter CategoriesExcludedAll(List<Guid> categoryIds) =>
        new(EventSubqueryFilterType.CategoriesExcludedAll, categoryIds);

    /// <summary>
    /// Filters events that have ALL specified tags assigned (AND logic).
    /// Each tag generates a correlated EXISTS subquery against EventTags.
    /// </summary>
    public static EventSubqueryFilter TagsIncludedAll(List<Guid> tagIds) =>
        new(EventSubqueryFilterType.TagsIncludedAll, tagIds);

    /// <summary>
    /// Filters events that have at least one of the specified tags assigned (OR logic).
    /// Uses a single IN clause against EventTags.
    /// </summary>
    public static EventSubqueryFilter TagsIncludedAny(List<Guid> tagIds) =>
        new(EventSubqueryFilterType.TagsIncludedAny, tagIds);

    /// <summary>
    /// Excludes events that have ANY of the specified tags (OR logic).
    /// Events with any excluded tag are filtered out.
    /// </summary>
    public static EventSubqueryFilter TagsExcludedAny(List<Guid> tagIds) =>
        new(EventSubqueryFilterType.TagsExcludedAny, tagIds);

    /// <summary>
    /// Excludes events only if they have ALL of the specified tags simultaneously (AND logic).
    /// Events are excluded only when every excluded tag is present.
    /// </summary>
    public static EventSubqueryFilter TagsExcludedAll(List<Guid> tagIds) =>
        new(EventSubqueryFilterType.TagsExcludedAll, tagIds);

    /// <summary>
    /// Filters events that have at least one session at the specified location.
    /// </summary>
    public static EventSubqueryFilter Location(Guid locationId) =>
        new(EventSubqueryFilterType.Location, locationId);

    /// <summary>
    /// Filters events that have at least one session at any of the specified locations (OR logic).
    /// </summary>
    public static EventSubqueryFilter Locations(List<Guid> locationIds) =>
        new(EventSubqueryFilterType.Locations, locationIds);

    /// <summary>
    /// Filters events that have at least one session in the specified language.
    /// </summary>
    public static EventSubqueryFilter Language(int languageId) =>
        new(EventSubqueryFilterType.Language, languageId);

    /// <summary>
    /// Filters events that have at least one session in any of the specified languages (OR logic).
    /// </summary>
    public static EventSubqueryFilter Languages(List<int> languageIds) =>
        new(EventSubqueryFilterType.Languages, languageIds);

    /// <summary>
    /// Filters events that have at least one session with the specified registration mode.
    /// </summary>
    public static EventSubqueryFilter RegistrationMode(int registrationModeId) =>
        new(EventSubqueryFilterType.RegistrationMode, registrationModeId);

    /// <summary>
    /// Filters events that have at least one session with any of the specified registration modes (OR logic).
    /// </summary>
    public static EventSubqueryFilter RegistrationModes(List<int> registrationModeIds) =>
        new(EventSubqueryFilterType.RegistrationModes, registrationModeIds);

    /// <summary>
    /// Filters out events that have already finished (last session has started).
    /// </summary>
    public static EventSubqueryFilter FutureOnly(DateTimeOffset now) =>
        new(EventSubqueryFilterType.FutureOnly, now);

    /// <summary>
    /// Filters public discovery lists to events with at least one published scheduled session that has not ended yet.
    /// </summary>
    public static EventSubqueryFilter CurrentOrUpcomingPublishedSession() =>
        new(EventSubqueryFilterType.CurrentOrUpcomingPublishedSession, true);

    public static EventSubqueryFilter Occurrence(EventOccurrenceDiscoveryFilter filter) =>
        new(EventSubqueryFilterType.Occurrence, filter);

    /// <summary>
    /// Filters events based on their temporal status relative to Now.
    /// </summary>
    public static EventSubqueryFilter Temporal(TemporalView view, DateTimeOffset now) =>
        new(EventSubqueryFilterType.TemporalView, (view, now));
}

/// <summary>
/// Enumeration of subquery filter types that require DbContext-level access.
/// </summary>
public enum EventSubqueryFilterType
{
    /// <summary>All public occurrence constraints apply to the same session.</summary>
    Occurrence,

    /// <summary>Category filter via EventCategories junction table.</summary>
    Category,

    /// <summary>Include events that have ALL specified categories (AND).</summary>
    CategoriesIncludedAll,

    /// <summary>Include events that have at least one specified category (OR).</summary>
    CategoriesIncludedAny,

    /// <summary>Exclude events that have ANY specified category (OR).</summary>
    CategoriesExcludedAny,

    /// <summary>Exclude events only if they have ALL specified categories (AND).</summary>
    CategoriesExcludedAll,

    /// <summary>Include events that have ALL specified tags (AND).</summary>
    TagsIncludedAll,

    /// <summary>Include events that have at least one specified tag (OR).</summary>
    TagsIncludedAny,

    /// <summary>Exclude events that have ANY specified tag (OR).</summary>
    TagsExcludedAny,

    /// <summary>Exclude events only if they have ALL specified tags (AND).</summary>
    TagsExcludedAll,

    /// <summary>Location filter via EventSessions table.</summary>
    Location,

    /// <summary>Location multi-value filter via EventSessions table (OR logic).</summary>
    Locations,

    /// <summary>Language filter via EventSessions → EventSessionLanguages tables.</summary>
    Language,

    /// <summary>Language multi-value filter via EventSessions → EventSessionLanguages tables (OR logic).</summary>
    Languages,

    /// <summary>Registration mode filter via EventSessions table.</summary>
    RegistrationMode,

    /// <summary>Registration mode multi-value filter via EventSessions table (OR logic).</summary>
    RegistrationModes,

    /// <summary>Filters out events where the last session has already started.</summary>
    FutureOnly,

    /// <summary>Requires at least one public, scheduled session whose end time has not passed.</summary>
    CurrentOrUpcomingPublishedSession,

    /// <summary>Filters events by temporal status (Upcoming, Ongoing, Past).</summary>
    TemporalView
}
