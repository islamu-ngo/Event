using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Scheduling;
using Explore.Application.Services.Webhooks;
using Explore.Application.Telemetry;
using Explore.Infrastructure.Webhooks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.HealthChecks;

public sealed class QueueDrainReadinessHealthCheck(
    IServiceScopeFactory scopeFactory,
    IOptions<IntegrationSyncProcessorSettings> integrationSettings,
    IOptions<IncomingWebhookProcessingSettings> incomingSettings,
    IOptions<WebhookBulkReplaySettings> replaySettings,
    IOptions<WebhookProviderPublicationProcessorSettings> publicationSettings,
    IOptions<PdsSyncSettings> pdsSettings,
    BusinessMetrics metrics,
    TimeProvider timeProvider) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        DateTime observedAt = timeProvider.GetUtcNow().UtcDateTime;
        QueueDrainHealthSnapshot snapshot;
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IQueueDrainHealthRepository>();
            snapshot = await repository.GetSnapshotAsync(
                observedAt,
                observedAt.AddSeconds(-integrationSettings.Value.ProcessingLeaseTimeoutSeconds),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            RecordAll("unhealthy", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            return HealthCheckResult.Unhealthy("Queue-drain readiness query failed.");
        }

        var data = new Dictionary<string, object>
        {
            ["observedAtUtc"] = observedAt,
            ["integrationDue"] = snapshot.IntegrationDue,
            ["integrationStale"] = snapshot.IntegrationStale,
            ["integrationAmbiguous"] = snapshot.IntegrationAmbiguous,
            ["incomingDue"] = snapshot.IncomingDue,
            ["incomingStale"] = snapshot.IncomingStale,
            ["bulkReplayQueued"] = snapshot.BulkReplayQueued,
            ["bulkReplayExecuting"] = snapshot.BulkReplayExecuting,
            ["providerPublicationDue"] = snapshot.ProviderPublicationDue,
            ["providerPublicationStale"] = snapshot.ProviderPublicationStale,
            ["providerPublicationUnknown"] = snapshot.ProviderPublicationUnknown,
            ["pdsDue"] = snapshot.PdsDue,
            ["pdsStale"] = snapshot.PdsStale,
            ["pdsDeadLettered"] = snapshot.PdsDeadLettered
        };

        bool integrationDegraded = IsIntegrationDegraded(snapshot);
        bool incomingDegraded = IsIncomingDegraded(snapshot);
        bool replayDegraded = IsReplayDegraded(snapshot);
        bool publicationDegraded = IsPublicationDegraded(snapshot);
        bool pdsDegraded = IsPdsDegraded(snapshot);
        Record(
            ScheduledJobNames.IntegrationSyncDrain,
            integrationSettings.Value.Enabled,
            integrationDegraded,
            snapshot.IntegrationDue,
            snapshot.IntegrationStale + snapshot.IntegrationAmbiguous);
        Record(
            ScheduledJobNames.IncomingWebhookIntakeDrain,
            incomingSettings.Value.Enabled,
            incomingDegraded,
            snapshot.IncomingDue,
            snapshot.IncomingStale);
        Record(
            ScheduledJobNames.WebhookBulkReplayDrain,
            replaySettings.Value.Enabled,
            replayDegraded,
            snapshot.BulkReplayQueued,
            snapshot.BulkReplayExecuting);
        Record(
            ScheduledJobNames.WebhookProviderPublicationDrain,
            publicationSettings.Value.Enabled,
            publicationDegraded,
            snapshot.ProviderPublicationDue,
            snapshot.ProviderPublicationStale + snapshot.ProviderPublicationUnknown);
        Record(
            ScheduledJobNames.PdsSyncDrain,
            pdsSettings.Value.Enabled,
            pdsDegraded,
            snapshot.PdsDue,
            snapshot.PdsStale + snapshot.PdsDeadLettered);

        bool degraded =
            (!integrationSettings.Value.Enabled || integrationDegraded) ||
            (!incomingSettings.Value.Enabled || incomingDegraded) ||
            (!replaySettings.Value.Enabled || replayDegraded) ||
            (publicationSettings.Value.Enabled && publicationDegraded) ||
            (!pdsSettings.Value.Enabled || pdsDegraded);
        return degraded
            ? HealthCheckResult.Degraded("One or more scheduler-owned queue drains require attention.", data: data)
            : HealthCheckResult.Healthy("Scheduler-owned queue drains are healthy.", data);
    }

    private bool IsIntegrationDegraded(QueueDrainHealthSnapshot snapshot) =>
        snapshot.IntegrationDue >= integrationSettings.Value.HealthDueWarningThreshold ||
        snapshot.IntegrationStale >= integrationSettings.Value.HealthStaleWarningThreshold ||
        snapshot.IntegrationAmbiguous >= integrationSettings.Value.HealthAmbiguousWarningThreshold;

    private bool IsIncomingDegraded(QueueDrainHealthSnapshot snapshot) =>
        snapshot.IncomingDue >= incomingSettings.Value.IntakeBacklogWarningThreshold ||
        snapshot.IncomingStale >= incomingSettings.Value.IntakeStaleLeaseWarningThreshold;

    private bool IsReplayDegraded(QueueDrainHealthSnapshot snapshot) =>
        snapshot.BulkReplayQueued >= replaySettings.Value.HealthQueuedWarningThreshold ||
        snapshot.BulkReplayExecuting >= replaySettings.Value.HealthExecutingWarningThreshold;

    private bool IsPublicationDegraded(QueueDrainHealthSnapshot snapshot) =>
        snapshot.ProviderPublicationDue >= publicationSettings.Value.HealthDueWarningThreshold ||
        snapshot.ProviderPublicationStale >= publicationSettings.Value.HealthStaleWarningThreshold ||
        snapshot.ProviderPublicationUnknown >= publicationSettings.Value.HealthUnknownWarningThreshold;

    private bool IsPdsDegraded(QueueDrainHealthSnapshot snapshot) =>
        snapshot.PdsDue >= pdsSettings.Value.HealthDueWarningThreshold ||
        snapshot.PdsStale >= pdsSettings.Value.HealthStaleWarningThreshold ||
        snapshot.PdsDeadLettered >= pdsSettings.Value.HealthDeadLetterWarningThreshold;

    private void Record(string jobName, bool enabled, bool degraded, int backlog, int stale) =>
        metrics.RecordQueueDrainHealth(jobName, !enabled ? "disabled" : degraded ? "degraded" : "healthy", backlog, stale);

    private void RecordAll(
        string outcome,
        int integrationBacklog,
        int integrationStale,
        int incomingBacklog,
        int incomingStale,
        int replayBacklog,
        int replayStale,
        int publicationBacklog,
        int publicationStale,
        int pdsBacklog,
        int pdsStale)
    {
        metrics.RecordQueueDrainHealth(ScheduledJobNames.IntegrationSyncDrain, outcome, integrationBacklog, integrationStale);
        metrics.RecordQueueDrainHealth(ScheduledJobNames.IncomingWebhookIntakeDrain, outcome, incomingBacklog, incomingStale);
        metrics.RecordQueueDrainHealth(ScheduledJobNames.WebhookBulkReplayDrain, outcome, replayBacklog, replayStale);
        metrics.RecordQueueDrainHealth(ScheduledJobNames.WebhookProviderPublicationDrain, outcome, publicationBacklog, publicationStale);
        metrics.RecordQueueDrainHealth(ScheduledJobNames.PdsSyncDrain, outcome, pdsBacklog, pdsStale);
    }
}
