using Explore.Domain.Enums;

namespace Explore.Application.Specifications.EventSessions;

/// <summary>
/// Represents a projection-backed filter for session custom property discovery queries.
/// These filters are applied at the repository level via correlated subqueries
/// against the <c>EventSessionCustomPropertyProjections</c> DbSet.
/// </summary>
/// <remarks>
/// Layer 3 filters are gated behind the <c>custom_properties.projection_discovery_enabled</c>
/// tenant setting. They do NOT touch raw EAV value tables — only pre-computed projections.
/// </remarks>
public sealed class EventSessionCustomPropertyProjectionFilter
{
    /// <summary>
    /// Gets the type of projection filter operation.
    /// </summary>
    public EventSessionCustomPropertyProjectionFilterType FilterType { get; }

    /// <summary>
    /// Gets the filter value (varies by filter type).
    /// </summary>
    public object Value { get; }

    /// <summary>
    /// Gets the maximum exposure level allowed for matching projection rows.
    /// </summary>
    public ExposureLevel ExposureCeiling { get; }

    private EventSessionCustomPropertyProjectionFilter(
        EventSessionCustomPropertyProjectionFilterType filterType,
        object value,
        ExposureLevel exposureCeiling)
    {
        FilterType = filterType;
        Value = value;
        ExposureCeiling = exposureCeiling;
    }

    /// <summary>
    /// Filters sessions that have a custom property projection matching the specified namespace and key
    /// with a normalized value equal to the specified value (case-insensitive via NormalizedValue).
    /// Only matches projections where <c>IsFilterable = true</c>.
    /// </summary>
    public static EventSessionCustomPropertyProjectionFilter ExactMatch(
        string @namespace,
        string key,
        string normalizedValue,
        ExposureLevel exposureCeiling = ExposureLevel.Public) =>
        new(EventSessionCustomPropertyProjectionFilterType.ExactMatch, (Namespace: @namespace, Key: key, NormalizedValue: normalizedValue), exposureCeiling);

    /// <summary>
    /// Filters sessions that have a custom property projection matching the specified namespace and key
    /// with a specific option selected. Only matches projections where <c>IsFilterable = true</c>.
    /// </summary>
    public static EventSessionCustomPropertyProjectionFilter OptionMatch(
        string @namespace,
        string key,
        Guid optionId,
        ExposureLevel exposureCeiling = ExposureLevel.Public) =>
        new(EventSessionCustomPropertyProjectionFilterType.OptionMatch, (Namespace: @namespace, Key: key, OptionId: optionId), exposureCeiling);

    /// <summary>
    /// Filters sessions that have a custom property projection matching the specified namespace and key
    /// with any of the specified options selected (OR logic).
    /// Only matches projections where <c>IsFilterable = true</c>.
    /// </summary>
    public static EventSessionCustomPropertyProjectionFilter OptionsMatchAny(
        string @namespace,
        string key,
        List<Guid> optionIds,
        ExposureLevel exposureCeiling = ExposureLevel.Public) =>
        new(EventSessionCustomPropertyProjectionFilterType.OptionsMatchAny, (Namespace: @namespace, Key: key, OptionIds: optionIds), exposureCeiling);

    /// <summary>
    /// Filters sessions that have a custom property projection matching the specified namespace and key
    /// where the NormalizedValue contains the search term (ILIKE '%term%').
    /// Only matches projections where <c>IsSearchable = true</c>.
    /// </summary>
    public static EventSessionCustomPropertyProjectionFilter TextSearch(
        string @namespace,
        string key,
        string searchTerm,
        ExposureLevel exposureCeiling = ExposureLevel.Public) =>
        new(EventSessionCustomPropertyProjectionFilterType.TextSearch, (Namespace: @namespace, Key: key, SearchTerm: searchTerm), exposureCeiling);

    /// <summary>
    /// Filters sessions that have any custom property projection where <c>IsSearchable = true</c>
    /// and the NormalizedValue contains the search term (cross-property text search).
    /// </summary>
    public static EventSessionCustomPropertyProjectionFilter GlobalTextSearch(
        string searchTerm,
        ExposureLevel exposureCeiling = ExposureLevel.Public) =>
        new(EventSessionCustomPropertyProjectionFilterType.GlobalTextSearch, searchTerm, exposureCeiling);

    /// <summary>
    /// Filters sessions that have a custom property projection matching the specified namespace and key
    /// (existence check — the property has a filterable value inside the exposure ceiling).
    /// </summary>
    public static EventSessionCustomPropertyProjectionFilter Exists(
        string @namespace,
        string key,
        ExposureLevel exposureCeiling = ExposureLevel.Public) =>
        new(EventSessionCustomPropertyProjectionFilterType.Exists, (Namespace: @namespace, Key: key), exposureCeiling);

    /// <summary>
    /// Filters sessions that have a custom property projection matching the specified namespace and key
    /// with a boolean value of <c>true</c>.
    /// Only matches projections where <c>IsFilterable = true</c>.
    /// </summary>
    public static EventSessionCustomPropertyProjectionFilter BooleanTrue(
        string @namespace,
        string key,
        ExposureLevel exposureCeiling = ExposureLevel.Public) =>
        new(EventSessionCustomPropertyProjectionFilterType.BooleanTrue, (Namespace: @namespace, Key: key), exposureCeiling);

    /// <summary>
    /// Filters sessions that have a custom property projection matching the specified namespace and key
    /// with a numeric value within the specified range (inclusive).
    /// Only matches projections where <c>IsFilterable = true</c>.
    /// </summary>
    public static EventSessionCustomPropertyProjectionFilter NumberRange(
        string @namespace,
        string key,
        decimal? min,
        decimal? max,
        ExposureLevel exposureCeiling = ExposureLevel.Public) =>
        new(EventSessionCustomPropertyProjectionFilterType.NumberRange, (Namespace: @namespace, Key: key, Min: min, Max: max), exposureCeiling);

    /// <summary>
    /// Filters sessions that have a custom property projection matching the specified namespace and key
    /// with a date/time value within the specified range (inclusive).
    /// Only matches projections where <c>IsFilterable = true</c>.
    /// </summary>
    public static EventSessionCustomPropertyProjectionFilter DateRange(
        string @namespace,
        string key,
        DateTimeOffset? from,
        DateTimeOffset? to,
        ExposureLevel exposureCeiling = ExposureLevel.Public) =>
        new(EventSessionCustomPropertyProjectionFilterType.DateRange, (Namespace: @namespace, Key: key, From: from, To: to), exposureCeiling);
}

/// <summary>
/// Enumeration of projection filter types for session custom property discovery queries.
/// Each type corresponds to a different subquery pattern in the repository.
/// </summary>
public enum EventSessionCustomPropertyProjectionFilterType
{
    /// <summary>Exact match on NormalizedValue where IsFilterable = true.</summary>
    ExactMatch,

    /// <summary>Option match on OptionId where IsFilterable = true.</summary>
    OptionMatch,

    /// <summary>Any-of-options match on OptionId where IsFilterable = true (OR logic).</summary>
    OptionsMatchAny,

    /// <summary>Text search on NormalizedValue where IsSearchable = true (ILIKE).</summary>
    TextSearch,

    /// <summary>Cross-property text search on any searchable projection (ILIKE).</summary>
    GlobalTextSearch,

    /// <summary>Existence check — property has a filterable projection row inside the exposure ceiling.</summary>
    Exists,

    /// <summary>Boolean true filter where IsFilterable = true.</summary>
    BooleanTrue,

    /// <summary>Numeric range filter where IsFilterable = true.</summary>
    NumberRange,

    /// <summary>Date/time range filter where IsFilterable = true.</summary>
    DateRange
}
