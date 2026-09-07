namespace Explore.Application.Analytics;

public sealed record AnalyticsEventDefinition(
    string EventName,
    IReadOnlySet<string> AllowedPropertyKeys,
    bool RequiresIdentifiedTracking = false);

public sealed record SanitizedAnalyticsTrackPayload(
    string DistinctId,
    string EventName,
    IReadOnlyDictionary<string, object> Properties);

public sealed record SanitizedAnalyticsPageViewPayload(
    string DistinctId,
    string PagePath,
    IReadOnlyDictionary<string, object> Properties);
