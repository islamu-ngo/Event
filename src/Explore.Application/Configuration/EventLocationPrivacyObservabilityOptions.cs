namespace Explore.Application.Configuration;

public sealed class EventLocationPrivacyObservabilityOptions
{
    public const string SectionName = "LocationPrivacy:Observability";
    public const int DefaultReviewQueueDegradedThreshold = 50;
    public const int MinReviewQueueDegradedThreshold = 1;
    public const int MaxReviewQueueDegradedThreshold = 100_000;

    /// <summary>
    /// Inclusive backlog size that is still considered healthy. Depth strictly greater than this value
    /// degrades readiness so operators remediate before erased venues linger on organizer surfaces.
    /// </summary>
    public int ReviewQueueDegradedThreshold { get; set; } = DefaultReviewQueueDegradedThreshold;

    public static bool IsValidReviewQueueDegradedThreshold(int threshold) =>
        threshold is >= MinReviewQueueDegradedThreshold and <= MaxReviewQueueDegradedThreshold;
}
