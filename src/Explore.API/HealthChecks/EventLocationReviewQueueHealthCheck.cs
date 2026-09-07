using Explore.Application.Contracts.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Explore.API.HealthChecks;

public sealed class EventLocationReviewQueueHealthCheck(
    IEventLocationReviewQueueMonitor monitor) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            EventLocationReviewQueueSnapshot snapshot = await monitor.GetSnapshotAsync(cancellationToken);
            var data = new Dictionary<string, object>
            {
                ["reviewQueueDepth"] = snapshot.Depth,
                ["degradedThreshold"] = snapshot.DegradedThreshold
            };

            return snapshot.ExceedsThreshold
                ? HealthCheckResult.Degraded("event_location_privacy_review_backlog", data: data)
                : HealthCheckResult.Healthy("event_location_privacy_review_within_threshold", data);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("event_location_privacy_review_queue_unavailable");
        }
    }
}
