using Explore.Application.Configuration;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Telemetry;
using Microsoft.Extensions.Options;

namespace Explore.Application.Services;

public sealed class EventLocationReviewQueueMonitor(
    IEventLocationRepository eventLocations,
    EventLocationPrivacyMetrics metrics,
    IOptions<EventLocationPrivacyObservabilityOptions> options) : IEventLocationReviewQueueMonitor
{
    public async Task<EventLocationReviewQueueSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        int depth = await eventLocations.CountNeedingPrivacyReviewAsync(cancellationToken);
        metrics.RecordReviewQueueDepth(depth);
        return new EventLocationReviewQueueSnapshot(
            depth,
            options.Value.ReviewQueueDegradedThreshold);
    }
}
