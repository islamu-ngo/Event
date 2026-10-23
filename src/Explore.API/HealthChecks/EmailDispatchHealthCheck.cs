using Explore.API.Configuration;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Telemetry;
using Explore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Explore.API.HealthChecks;

public sealed class EmailDispatchHealthCheck(
    IOptions<EmailDispatchProcessorSettings> options,
    IOptions<QuartzSchedulerSettings> schedulerOptions,
    IServiceScopeFactory scopeFactory,
    BusinessMetrics metrics) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var scheduler = schedulerOptions.Value;
        var data = new Dictionary<string, object>
        {
            ["enabled"] = settings.Enabled,
            ["mode"] = settings.Mode.ToString(),
            ["pollingIntervalSeconds"] = settings.PollingIntervalSeconds,
            ["batchSize"] = settings.BatchSize,
            ["maxAttemptCount"] = settings.MaxAttemptCount,
            ["processingLeaseTimeoutSeconds"] = settings.ProcessingLeaseTimeoutSeconds,
            ["dueDispatchWarningThreshold"] = settings.HealthDueDispatchWarningThreshold,
            ["staleProcessingWarningThreshold"] = settings.HealthStaleProcessingWarningThreshold,
            ["unknownWarningThreshold"] = settings.HealthUnknownWarningThreshold,
            ["deadLetterWarningThreshold"] = settings.HealthDeadLetterWarningThreshold,
            ["oldestPendingWarningSeconds"] = settings.HealthOldestPendingWarningSeconds,
            ["tenantBacklogWarningThreshold"] = settings.HealthTenantBacklogWarningThreshold,
            ["consumerId"] = settings.ConsumerId,
            ["schedulerEnabled"] = scheduler.Enabled,
            ["schedulerPersistentStore"] = scheduler.UsePersistentStore,
            ["schedulerStatusEndpointEnabled"] = scheduler.StatusEndpointEnabled
        };

        if (!settings.Enabled)
        {
            return HealthCheckResult.Healthy(
                "Basic email dispatch is intentionally disabled.",
                data: data);
        }

        if (settings.Mode == EmailDispatchProcessorMode.Disabled)
        {
            return HealthCheckResult.Healthy(
                "Basic email dispatch scheduler mode is Disabled.",
                data: data);
        }

        if (settings.Mode == EmailDispatchProcessorMode.Quartz && !scheduler.Enabled)
        {
            return HealthCheckResult.Degraded(
                "Basic email dispatch is configured for Quartz, but the Quartz scheduler is disabled.",
                data: data);
        }

        var now = DateTime.UtcNow;
        var processingStartedBefore = now.AddSeconds(-settings.ProcessingLeaseTimeoutSeconds);
        await using var scope = scopeFactory.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IEmailDispatchHealthSnapshotReader>();
        var snapshot = await reader.ReadAsync(
            now, processingStartedBefore, settings.HealthTenantSampleLimit, cancellationToken);
        var dueDispatchCount = snapshot.DueDispatchCount;
        var retryScheduledCount = snapshot.RetryScheduledCount;
        var staleProcessingCount = snapshot.StaleProcessingCount;
        var deadLetteredCount = snapshot.DeadLetteredCount;
        var unknownCount = snapshot.UnknownCount;
        var parkedCount = snapshot.ParkedCount;
        var optionalReminderDeferralActive = snapshot.OptionalReminderDeferralActive;
        var oldestDueCreatedAt = snapshot.OldestDueCreatedAt;
        var tenantBacklog = snapshot.TenantBacklogSample;
        var oldestPendingAgeSeconds = oldestDueCreatedAt is null
            ? 0d
            : Math.Max(0d, (now - oldestDueCreatedAt.Value).TotalSeconds);

        data["dueDispatchCount"] = dueDispatchCount;
        data["retryScheduledCount"] = retryScheduledCount;
        data["staleProcessingCount"] = staleProcessingCount;
        data["deadLetteredCount"] = deadLetteredCount;
        data["unknownCount"] = unknownCount;
        data["parkedCount"] = parkedCount;
        data["optionalReminderDeferralActive"] = optionalReminderDeferralActive;
        data["globalPaused"] = snapshot.GlobalPaused;
        data["globalSmtpRateLimitOverrideActive"] = snapshot.GlobalSmtpRateLimitOverrideActive;
        data["processingStartedBefore"] = processingStartedBefore;
        data["oldestActivePendingAgeSeconds"] = oldestPendingAgeSeconds;
        data["tenantBacklogSample"] = tenantBacklog;
        metrics.RecordEmailDispatchOldestPendingAge(oldestPendingAgeSeconds);
        metrics.RecordEmailDispatchOptionalReminderDeferral(optionalReminderDeferralActive);
        var sampleRank = 0;
        foreach (var count in tenantBacklog
                     .OrderByDescending(entry => entry.Value)
                     .ThenBy(entry => entry.Key)
                     .Select(entry => entry.Value))
        {
            metrics.RecordEmailDispatchTenantBacklog(++sampleRank, count);
        }

        if (snapshot.GlobalPaused)
        {
            return HealthCheckResult.Degraded(
                "Basic email dispatch is paused by an instance operator.",
                data: data);
        }

        if (staleProcessingCount >= settings.HealthStaleProcessingWarningThreshold)
        {
            return HealthCheckResult.Degraded(
                "Basic email dispatch has stale processing rows.",
                data: data);
        }

        if (deadLetteredCount >= settings.HealthDeadLetterWarningThreshold)
        {
            return HealthCheckResult.Degraded(
                "Basic email dispatch has dead-lettered rows.",
                data: data);
        }

        if (unknownCount >= settings.HealthUnknownWarningThreshold)
        {
            return HealthCheckResult.Degraded(
                "Basic email dispatch has rows requiring provider-outcome reconciliation.",
                data: data);
        }

        if (dueDispatchCount >= settings.HealthDueDispatchWarningThreshold)
        {
            return HealthCheckResult.Degraded(
                "Basic email dispatch due backlog is above the configured threshold.",
                data: data);
        }

        if (oldestPendingAgeSeconds >= settings.HealthOldestPendingWarningSeconds)
        {
            return HealthCheckResult.Degraded(
                "Basic email dispatch oldest pending row is above the configured age threshold.",
                data: data);
        }

        if (tenantBacklog.Values.Any(count => count >= settings.HealthTenantBacklogWarningThreshold))
        {
            return HealthCheckResult.Degraded(
                "Basic email dispatch tenant backlog is above the configured threshold.",
                data: data);
        }

        return HealthCheckResult.Healthy(
            settings.Mode == EmailDispatchProcessorMode.Quartz
                ? "Basic email dispatch is scheduled by Quartz."
                : "Basic email dispatch hosted service is enabled.",
            data);
    }
}
