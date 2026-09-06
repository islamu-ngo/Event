// ABOUTME: Quartz jobs for the platform's periodic maintenance sweeps.
// ABOUTME: Each job owns one iteration; the scheduler owns enablement, cadence, cancellation, and containment.

using Explore.Application.Contracts.Scheduling;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Webhooks;
using Quartz;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.OrganizerPaymentConnections;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Managed;
using Explore.Infrastructure;
using Explore.Application.Telemetry;

namespace Explore.API.Scheduling;

/// <summary>
/// These jobs replace hand-rolled <c>BackgroundService</c> timer loops. Each loop previously re-implemented
/// enablement checks, an initial delay, an interval wait, cancellation handling that had to distinguish
/// shutdown from failure, exception containment, and per-iteration scope creation — roughly fifty lines of
/// identical mechanics per worker, each free to drift.
/// <para>
/// Quartz owns all of that now. A job is the work of one pass and nothing else, and because the ADO job store
/// persists trigger state, a restart resumes the schedule instead of silently resetting it. Scheduling state
/// also becomes visible to operators through the scheduler status endpoint rather than being implicit in
/// process uptime.
/// </para>
/// <para>
/// <see cref="DisallowConcurrentExecutionAttribute"/> preserves the sequential guarantee the old
/// <c>while</c> loops had: a slow pass delays the next one rather than overlapping with it.
/// </para>
/// </summary>
[DisallowConcurrentExecution]
public sealed class IdempotencyCleanupJob(
    IIdempotencyCleanupService cleanupService,
    ILogger<IdempotencyCleanupJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        await cleanupService.CleanupExpiredAsync(DateTime.UtcNow, context.CancellationToken);
        logger.LogInformation("Scheduled job {JobName} completed.", ScheduledJobNames.IdempotencyCleanup);
    }
}

/// <inheritdoc cref="IdempotencyCleanupJob"/>
[DisallowConcurrentExecution]
public sealed class AtprotoTransientCleanupJob(
    AtprotoTransientCleanupService cleanupService,
    BusinessMetrics metrics,
    ILogger<AtprotoTransientCleanupJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            var result = await cleanupService.CleanupExpiredAsync(context.CancellationToken);
            metrics.RecordAtprotoTransientCleanup(true, result.TransientRows, result.ReplayRows);
        }
        catch
        {
            metrics.RecordAtprotoTransientCleanup(false);
            throw;
        }
        logger.LogInformation("Scheduled job {JobName} completed.", ScheduledJobNames.AtprotoTransientCleanup);
    }
}

/// <inheritdoc cref="IdempotencyCleanupJob"/>
[DisallowConcurrentExecution]
public sealed class AiRetentionCleanupJob(
    IAiRetentionCleanupService cleanupService,
    ILogger<AiRetentionCleanupJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        await cleanupService.CleanupAllTenantsAsync(DateTime.UtcNow, context.CancellationToken);
        logger.LogInformation("Scheduled job {JobName} completed.", ScheduledJobNames.AiRetentionCleanup);
    }
}

/// <inheritdoc cref="IdempotencyCleanupJob"/>
[DisallowConcurrentExecution]
public sealed class EmailDispatchRetentionCleanupJob(
    IEmailDispatchRetentionCleanupService cleanupService,
    ILogger<EmailDispatchRetentionCleanupJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        await cleanupService.CleanupAsync(DateTime.UtcNow, context.CancellationToken);
        logger.LogInformation("Scheduled job {JobName} completed.", ScheduledJobNames.EmailDispatchRetentionCleanup);
    }
}

/// <inheritdoc cref="IdempotencyCleanupJob"/>
[DisallowConcurrentExecution]
public sealed class WebhookRetentionCleanupJob(
    IWebhookRetentionCleanupService cleanupService,
    ILogger<WebhookRetentionCleanupJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        await cleanupService.CleanupAllTenantsAsync(DateTime.UtcNow, context.CancellationToken);
        logger.LogInformation("Scheduled job {JobName} completed.", ScheduledJobNames.WebhookRetentionCleanup);
    }
}

/// <inheritdoc cref="IdempotencyCleanupJob"/>
[DisallowConcurrentExecution]
public sealed class PrivacyErasureCredentialCleanupJob(
    IPrivacyErasureCredentialCleanupService cleanupService,
    ILogger<PrivacyErasureCredentialCleanupJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        await cleanupService.CleanupAsync(DateTime.UtcNow, context.CancellationToken);
        logger.LogInformation("Scheduled job {JobName} completed.", ScheduledJobNames.PrivacyErasureCredentialCleanup);
    }
}

/// <summary>
/// Retention deletion is driven per tenant because each tenant's immutable retention deadline is evaluated
/// against its own rows; the bounded batch size keeps one pass from holding a long transaction.
/// </summary>
[DisallowConcurrentExecution]
public sealed class RegistrationRetentionCleanupJob(
    ITenantRepository tenantRepository,
    IRegistrationRetentionCleanupRepository cleanupRepository,
    ILogger<RegistrationRetentionCleanupJob> logger) : IJob
{
    private const int TenantBatchSize = 500;

    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var deleted = 0;
        foreach (var tenant in await tenantRepository.GetActiveAsNoTrackingAsync(context.CancellationToken))
        {
            deleted += (await cleanupRepository.CleanupTenantAsync(
                tenant.Id,
                DateTime.UtcNow,
                TenantBatchSize,
                context.CancellationToken)).TotalDeleted;
        }

        logger.LogInformation(
            "Scheduled job {JobName} completed. DeletedRows={DeletedRows}",
            ScheduledJobNames.RegistrationRetentionCleanup,
            deleted);
    }
}

/// <inheritdoc cref="IdempotencyCleanupJob"/>
[DisallowConcurrentExecution]
public sealed class ConfigurationPortabilityRetentionCleanupJob(
    ConfigurationImportSessionManager sessions,
    IConfigurationImportArtifactStore artifacts,
    IConfigurationDirectTransferChunkStore transferChunks,
    ILogger<ConfigurationPortabilityRetentionCleanupJob> logger) : IJob
{
    private const int BatchSize = 1_000;

    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        DateTime occurredAt = DateTime.UtcNow;
        int expiredSessions = await sessions.ExpireAsync(
            occurredAt,
            BatchSize,
            context.CancellationToken);
        int expiredArtifacts = await artifacts.DeleteExpiredAsync(
            occurredAt,
            BatchSize,
            context.CancellationToken);
        int expiredChunks = await transferChunks.DeleteExpiredAsync(
            occurredAt,
            BatchSize,
            context.CancellationToken);
        logger.LogInformation(
            "Scheduled job {JobName} completed. ExpiredSessions={ExpiredSessions} ExpiredArtifacts={ExpiredArtifacts} ExpiredChunks={ExpiredChunks}",
            ScheduledJobNames.ConfigurationPortabilityRetentionCleanup,
            expiredSessions,
            expiredArtifacts,
            expiredChunks);
    }
}

/// <inheritdoc cref="IdempotencyCleanupJob"/>
[DisallowConcurrentExecution]
public sealed class OrganizerPaymentReadinessReconciliationJob(
    OrganizerPaymentReadinessReconciliationService reconciliationService,
    ILogger<OrganizerPaymentReadinessReconciliationJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var result = await reconciliationService.ReconcileOnceAsync(context.CancellationToken);
        if (result.ProcessedCount == 0 && result.FailureCount == 0)
        {
            return;
        }

        // Failure samples carry only the provider's own codes and request ids — never connection credentials.
        logger.LogInformation(
            "Scheduled job {JobName} processed {ProcessedCount}/{DueCount}; updated {UpdatedCount}, skipped {SkippedCount}, failures {FailureCount}; failure samples {FailureSamples}",
            ScheduledJobNames.OrganizerPaymentReadinessReconciliation,
            result.ProcessedCount,
            result.DueCount,
            result.UpdatedCount,
            result.SkippedCount,
            result.FailureCount,
            result.Failures.Select(failure => new { failure.FailureCode, failure.ProviderRequestId }).ToArray());
    }
}

/// <inheritdoc cref="IdempotencyCleanupJob"/>
[DisallowConcurrentExecution]
public sealed class StorageReconciliationJob(
    IStorageReconciliationService reconciliationService,
    ILogger<StorageReconciliationJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        await reconciliationService.ReconcileAsync(DateTime.UtcNow, context.CancellationToken);
        logger.LogInformation("Scheduled job {JobName} completed.", ScheduledJobNames.StorageReconciliation);
    }
}
