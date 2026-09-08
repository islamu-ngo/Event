using Explore.API.HealthChecks;
using Explore.Application.Contracts.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using TUnit.Core;

namespace ApiIntegrationTests.Privacy;

[Category("EventLocationPrivacy")]
public sealed class EventLocationReviewQueueHealthCheckTests
{
    [Test]
    [Arguments(0)]
    [Arguments(49)]
    [Arguments(50)]
    public async Task CheckHealthAsync_WithBacklogAtOrBelowThreshold_ReportsHealthy(int depth)
    {
        var healthCheck = new EventLocationReviewQueueHealthCheck(CreateMonitor(depth, 50));

        HealthCheckResult result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Description).IsEqualTo("event_location_privacy_review_within_threshold");
        await Assert.That(result.Data).ContainsKey("reviewQueueDepth").And.Value.IsEqualTo(depth);
        await Assert.That(result.Data).ContainsKey("degradedThreshold").And.Value.IsEqualTo(50);
    }

    [Test]
    public async Task CheckHealthAsync_WithBacklogAboveThreshold_ReportsDegraded()
    {
        var healthCheck = new EventLocationReviewQueueHealthCheck(CreateMonitor(51, 50));

        HealthCheckResult result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Description).IsEqualTo("event_location_privacy_review_backlog");
        await Assert.That(result.Data).ContainsKey("reviewQueueDepth").And.Value.IsEqualTo(51);
    }

    [Test]
    public async Task CheckHealthAsync_NeverExposesTenantEventOrVenueIdentity()
    {
        var healthCheck = new EventLocationReviewQueueHealthCheck(CreateMonitor(120, 50));

        HealthCheckResult result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Data.Keys)
            .DoesNotContain(key => key.Contains("tenant", StringComparison.OrdinalIgnoreCase));
        await Assert.That(result.Data.Keys)
            .DoesNotContain(key => key.Contains("event", StringComparison.OrdinalIgnoreCase));
        await Assert.That(result.Data.Keys)
            .DoesNotContain(key => key.Contains("location", StringComparison.OrdinalIgnoreCase));
        await Assert.That(result.Data.Keys)
            .DoesNotContain(key => key.Contains("address", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task CheckHealthAsync_WhenBacklogCannotBeRead_ReportsUnhealthyWithoutExceptionText()
    {
        var monitor = Substitute.For<IEventLocationReviewQueueMonitor>();
        monitor.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns<Task<EventLocationReviewQueueSnapshot>>(_ =>
                throw new InvalidOperationException("connection to 10.0.0.4:5432 refused"));
        var healthCheck = new EventLocationReviewQueueHealthCheck(monitor);

        HealthCheckResult result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Description).IsEqualTo("event_location_privacy_review_queue_unavailable");
        await Assert.That(result.Description).DoesNotContain("5432");
    }

    [Test]
    public async Task CheckHealthAsync_PropagatesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var monitor = Substitute.For<IEventLocationReviewQueueMonitor>();
        monitor.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns<Task<EventLocationReviewQueueSnapshot>>(_ => throw new OperationCanceledException());
        var healthCheck = new EventLocationReviewQueueHealthCheck(monitor);

        await Assert.That(async () =>
                await healthCheck.CheckHealthAsync(new HealthCheckContext(), cancellation.Token))
            .Throws<OperationCanceledException>();
    }

    private static IEventLocationReviewQueueMonitor CreateMonitor(int depth, int threshold)
    {
        var monitor = Substitute.For<IEventLocationReviewQueueMonitor>();
        monitor.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new EventLocationReviewQueueSnapshot(depth, threshold));
        return monitor;
    }
}
