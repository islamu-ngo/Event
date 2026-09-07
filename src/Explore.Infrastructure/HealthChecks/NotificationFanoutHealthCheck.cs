using Explore.Application.Contracts.Persistence;
using Explore.Application.Telemetry;
using Explore.Infrastructure.NotificationFanout;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.HealthChecks;

public sealed class NotificationFanoutHealthCheck(
    IOptions<NotificationFanoutProcessorSettings> options,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    BusinessMetrics metrics) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        NotificationFanoutProcessorSettings settings = options.Value;
        var data = new Dictionary<string, object>
        {
            ["enabled"] = settings.Enabled,
            ["pollingIntervalSeconds"] = settings.PollingIntervalSeconds,
            ["pageSize"] = settings.PageSize,
            ["maxClaimsPerRound"] = settings.MaxClaimsPerRound,
            ["maxActiveClaims"] = settings.MaxActiveClaims,
            ["maxActiveClaimsPerTenant"] = settings.MaxActiveClaimsPerTenant,
            ["claimLeaseSeconds"] = settings.ClaimLeaseSeconds,
            ["dueOccurrenceWarningThreshold"] = settings.HealthDueOccurrenceWarningThreshold,
            ["expiredClaimWarningThreshold"] = settings.HealthExpiredClaimWarningThreshold,
            ["oldestDueWarningSeconds"] = settings.HealthOldestDueWarningSeconds
        };

        if (!settings.Enabled)
        {
            return HealthCheckResult.Degraded(
                "Notification fanout processing is intentionally disabled.",
                data: data);
        }

        DateTime observedAt = timeProvider.GetUtcNow().UtcDateTime;
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<INotificationFanoutRunRepository>();
        NotificationFanoutProcessorSnapshot snapshot = await repository.GetProcessorSnapshotAsync(
            observedAt,
            cancellationToken);
        long oldestDueAgeSeconds = snapshot.OldestDueAt.HasValue
            ? Math.Max(0L, (long)(observedAt - snapshot.OldestDueAt.Value).TotalSeconds)
            : 0L;
        int remainingOccurrenceCount = checked(snapshot.DueOccurrenceCount + snapshot.ActiveClaimCount);

        data["dueOccurrenceCount"] = snapshot.DueOccurrenceCount;
        data["dueCoreOccurrenceCount"] = snapshot.DueCoreOccurrenceCount;
        data["dueOptionalReminderCount"] = snapshot.DueOptionalReminderCount;
        data["activeClaimCount"] = snapshot.ActiveClaimCount;
        data["expiredClaimCount"] = snapshot.ExpiredClaimCount;
        data["remainingOccurrenceCount"] = remainingOccurrenceCount;
        data["processedRecipientCount"] = snapshot.ProcessedRecipientCount;
        data["supersededOccurrenceCount"] = snapshot.SupersededOccurrenceCount;
        data["oldestDueAgeSeconds"] = oldestDueAgeSeconds;
        data["optionalReminderDeferralActive"] = snapshot.OptionalRemindersDeferred;

        metrics.RecordNotificationFanoutProcessorSnapshot(
            snapshot.DueOccurrenceCount,
            snapshot.DueCoreOccurrenceCount,
            snapshot.DueOptionalReminderCount,
            snapshot.ActiveClaimCount,
            snapshot.ExpiredClaimCount,
            snapshot.SupersededOccurrenceCount,
            snapshot.ProcessedRecipientCount,
            oldestDueAgeSeconds,
            snapshot.OptionalRemindersDeferred);

        if (snapshot.ExpiredClaimCount >= settings.HealthExpiredClaimWarningThreshold)
        {
            return HealthCheckResult.Degraded(
                "Notification fanout has expired processing claims.",
                data: data);
        }

        if (snapshot.DueOccurrenceCount >= settings.HealthDueOccurrenceWarningThreshold)
        {
            return HealthCheckResult.Degraded(
                "Notification fanout due backlog is above the configured threshold.",
                data: data);
        }

        if (oldestDueAgeSeconds >= settings.HealthOldestDueWarningSeconds)
        {
            return HealthCheckResult.Degraded(
                "Notification fanout oldest due occurrence is above the configured age threshold.",
                data: data);
        }

        return HealthCheckResult.Healthy("Notification fanout processing is enabled.", data);
    }
}
