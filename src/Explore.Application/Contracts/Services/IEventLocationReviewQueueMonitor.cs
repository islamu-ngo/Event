namespace Explore.Application.Contracts.Services;

/// <summary>
/// Aggregate, identifier-free snapshot of the privacy remediation backlog.
/// </summary>
/// <param name="Depth">Live EventLocations still flagged <c>NeedsPrivacyReview</c> across all tenants.</param>
/// <param name="DegradedThreshold">Inclusive backlog size that is still considered healthy.</param>
public sealed record EventLocationReviewQueueSnapshot(int Depth, int DegradedThreshold)
{
    public bool ExceedsThreshold => Depth > DegradedThreshold;
}

public interface IEventLocationReviewQueueMonitor
{
    /// <summary>
    /// Counts the current remediation backlog and publishes it to the privacy meter as a side effect,
    /// so the gauge tracks whatever cadence the readiness probe is scraped at.
    /// </summary>
    Task<EventLocationReviewQueueSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}
