using System.Runtime.ExceptionServices;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Scheduling;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Services.Moderation;
using Microsoft.Extensions.Options;
using Quartz;

namespace Explore.API.Scheduling;

/// <summary>One bounded pass; each contribution gets a fresh context, real credential, and native authority acquisition.</summary>
[DisallowConcurrentExecution]
[PersistJobDataAfterExecution]
public sealed class ModerationReconciliationJob(
    IServiceProvider services, IOptionsMonitor<ModerationReconciliationOptions> options, TimeProvider clock,
    ILogger<ModerationReconciliationJob> logger) : IJob
{
    private const string CursorKey = "last-scanned-sanction";
    private const string EffectCursorKey = "last-scanned-effect";
    private const string NoticeCleanupCursorKey = "last-scanned-notice-approval";
    private const string ResourceReviewCursorKey = "last-scanned-resource-quarantine";
    private const string ScheduleRiskCursorKey = "last-scanned-schedule-risk-review";
    private const string CursorTenantKey = "cursor-tenant";

    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var settings = options.CurrentValue;
        if (!settings.Enabled) return;
        var tenantId = settings.TenantId;
        if (tenantId == Guid.Empty || settings.BatchSize is < 1 or > 1000)
            throw new InvalidOperationException("Moderation reconciliation requires explicit tenancy and a bounded batch.");
        var token = context.CancellationToken;
        ExceptionDispatchInfo? failure = null;
        async Task RunStageAsync(Func<Task> execute)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                await execute();
            }
            catch (Exception exception)
            {
                token.ThrowIfCancellationRequested();
                failure ??= ExceptionDispatchInfo.Capture(exception);
                // Do not log private owner/credential exception details or report a successful fire.
                logger.LogWarning("Moderation reconciliation stage failed; continuing independent bounded stages.");
            }
        }
        var data = context.JobDetail.JobDataMap;
        var sameTenant = data.GetString(CursorTenantKey) == tenantId.ToString("N");
        if (!sameTenant)
        {
            // Quartz persists this map even after a failed fire. Move the whole cursor
            // generation before any partial pass can stamp the newly configured tenant.
            data[CursorKey] = string.Empty;
            data[EffectCursorKey] = string.Empty;
            data[NoticeCleanupCursorKey] = string.Empty;
            data[ResourceReviewCursorKey] = string.Empty;
            data[ScheduleRiskCursorKey] = string.Empty;
            data[CursorTenantKey] = tenantId.ToString("N");
        }
        var after = sameTenant
            && Guid.TryParse(data.GetString(CursorKey), out var cursor) ? cursor : (Guid?)null;
        var afterNotice = sameTenant
            && Guid.TryParse(data.GetString(NoticeCleanupCursorKey), out var noticeCursor) ? noticeCursor : (Guid?)null;
        // Each owner gets a fresh scope: a failed database context must not poison another stage.
        await RunStageAsync(async () =>
        {
            await using var cleanup = services.CreateAsyncScope();
            var tenant = cleanup.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
            tenant.SetTenant(tenantId);
            try
            {
                await cleanup.ServiceProvider.GetRequiredService<IModerationPreFindingRepository>()
                    .PurgeExpiredPreFindingAsync(Math.Min(settings.BatchSize, 100), token);
            }
            finally
            {
                tenant.Clear();
            }
        });
        await RunStageAsync(async () =>
        {
            await using var cleanup = services.CreateAsyncScope();
            var tenant = cleanup.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
            tenant.SetTenant(tenantId);
            try
            {
                await cleanup.ServiceProvider.GetRequiredService<IModerationCaseNarrativeCustody>()
                    .PurgeExpiredAsync(Math.Min(settings.BatchSize, 100), token);
            }
            finally
            {
                tenant.Clear();
            }
        });
        await RunStageAsync(async () =>
        {
            await using (var cleanup = services.CreateAsyncScope())
            {
                var tenant = cleanup.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
                var machine = cleanup.ServiceProvider.GetRequiredService<IMachinePrincipalExecutionAccessor>();
                tenant.SetTenant(tenantId);
                try
                {
                    var credential = await cleanup.ServiceProvider.GetRequiredService<ModerationReconciliationCredentialProvider>()
                        .AuthenticateAsync(tenantId, token);
                    if (credential is null) throw new InvalidOperationException("Moderation maintenance credential is unavailable.");
                    machine.SetPrincipal(credential);
                    var result = await cleanup.ServiceProvider.GetRequiredService<IModerationNoticeCleanupRepository>()
                        .PruneAsync(afterNotice, Math.Min(settings.BatchSize, 100), token);
                    if (result.Outcome == ModerationNoticeCleanupOutcome.Applied)
                    {
                        data[CursorTenantKey] = tenantId.ToString("N");
                        data[NoticeCleanupCursorKey] = result.LastScannedApprovalId?.ToString("N") ?? string.Empty;
                    }
                    logger.LogInformation("Moderation notice cleanup outcome={Outcome}.", result.Outcome);
                }
                finally
                {
                    machine.Clear();
                    tenant.Clear();
                }
            }
        });
        await RunStageAsync(async () =>
        {
            ModerationReconciliationBatch batch;
            await using (var discovery = services.CreateAsyncScope())
            {
                var tenant = discovery.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
                var machine = discovery.ServiceProvider.GetRequiredService<IMachinePrincipalExecutionAccessor>();
                tenant.SetTenant(tenantId);
                try
                {
                    var credential = await discovery.ServiceProvider.GetRequiredService<ModerationReconciliationCredentialProvider>()
                        .AuthenticateAsync(tenantId, token);
                    if (credential is null) throw new InvalidOperationException("Moderation maintenance credential is unavailable.");
                    machine.SetPrincipal(credential);
                    batch = await discovery.ServiceProvider.GetRequiredService<IModerationReconciliationQuery>()
                        .ReadDueAsync(tenantId, clock.GetUtcNow(), settings.BatchSize, after, token);
                }
                finally
                {
                    machine.Clear();
                    tenant.Clear();
                }
            }

            foreach (var target in batch.Targets)
            {
                token.ThrowIfCancellationRequested();
                await using var execution = services.CreateAsyncScope();
                var tenant = execution.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
                var machine = execution.ServiceProvider.GetRequiredService<IMachinePrincipalExecutionAccessor>();
                tenant.SetTenant(target.TenantId);
                try
                {
                    var credential = await execution.ServiceProvider.GetRequiredService<ModerationReconciliationCredentialProvider>()
                        .AuthenticateAsync(target.TenantId, token);
                    if (credential is null) throw new InvalidOperationException("Moderation maintenance credential is unavailable.");
                    machine.SetPrincipal(credential);
                    var result = await execution.ServiceProvider
                        .GetRequiredService<ICommandHandler<ReconcileModerationSanctionCommand, ModerationLifecycleResponse>>()
                        .ExecuteAsync(target.Command(), token);
                    logger.LogInformation("Moderation reconciliation outcome={Outcome}.",
                        result.IsSuccess ? "Known" : result.FailureCode);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    // Exceptions may contain database parameters or credential-provider details.
                    // Retain neither them nor target identifiers in operational logs.
                    logger.LogWarning("Moderation reconciliation failed for one contribution; continuing the bounded pass.");
                }
                finally
                {
                    machine.Clear();
                    tenant.Clear();
                }
            }
            data[CursorKey] = batch.LastScannedSanctionId?.ToString("N") ?? string.Empty;
        });
        var afterResourceReview = sameTenant
            && Guid.TryParse(data.GetString(ResourceReviewCursorKey), out var resourceReviewCursor)
            ? resourceReviewCursor : (Guid?)null;
        await RunStageAsync(async () =>
        {
            await using (var review = services.CreateAsyncScope())
            {
                var tenant = review.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
                var machine = review.ServiceProvider.GetRequiredService<IMachinePrincipalExecutionAccessor>();
                tenant.SetTenant(tenantId);
                try
                {
                    var credential = await review.ServiceProvider.GetRequiredService<ModerationReconciliationCredentialProvider>()
                        .AuthenticateAsync(tenantId, token);
                    if (credential is null) throw new InvalidOperationException("Moderation maintenance credential is unavailable.");
                    machine.SetPrincipal(credential);
                    var result = await review.ServiceProvider.GetRequiredService<IModerationResourceQuarantineDutyRepository>()
                        .ExecutePassAsync(afterResourceReview, Math.Min(settings.BatchSize, 100), token);
                    if (result.Available)
                        data[ResourceReviewCursorKey] = result.LastScannedQuarantineId?.ToString("N") ?? string.Empty;
                }
                finally
                {
                    machine.Clear();
                    tenant.Clear();
                }
            }
        });
        var afterScheduleRisk = sameTenant
            && Guid.TryParse(data.GetString(ScheduleRiskCursorKey), out var scheduleRiskCursor)
            ? scheduleRiskCursor : (Guid?)null;
        await RunStageAsync(async () =>
        {
            await using (var risk = services.CreateAsyncScope())
            {
                var tenant = risk.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
                var machine = risk.ServiceProvider.GetRequiredService<IMachinePrincipalExecutionAccessor>();
                tenant.SetTenant(tenantId);
                try
                {
                    var credential = await risk.ServiceProvider.GetRequiredService<ModerationReconciliationCredentialProvider>()
                        .AuthenticateAsync(tenantId, token);
                    if (credential is null) throw new InvalidOperationException("Moderation maintenance credential is unavailable.");
                    machine.SetPrincipal(credential);
                    var result = await risk.ServiceProvider
                        .GetRequiredService<IModerationScheduleRiskReviewReconciliationRepository>()
                        .ReconcileScheduleRiskAsync(afterScheduleRisk, Math.Min(settings.BatchSize, 100), token);
                    if (result.Available)
                        data[ScheduleRiskCursorKey] = result.LastScannedReviewId?.ToString("N") ?? string.Empty;
                }
                finally
                {
                    machine.Clear();
                    tenant.Clear();
                }
            }
        });
        var afterEffect = sameTenant
            && Guid.TryParse(data.GetString(EffectCursorKey), out var effectCursor) ? effectCursor : (Guid?)null;
        await RunStageAsync(async () =>
        {
            await using (var recovery = services.CreateAsyncScope())
            {
                var lastEffect = await recovery.ServiceProvider.GetRequiredService<ModerationEffectRecoveryService>()
                    .ExecutePassAsync(tenantId, settings.BatchSize, afterEffect, token);
                data[EffectCursorKey] = lastEffect?.ToString("N") ?? string.Empty;
            }
        });
        token.ThrowIfCancellationRequested();
        failure?.Throw();
        logger.LogInformation("Scheduled job {JobName} completed.", ScheduledJobNames.ModerationReconciliation);
    }
}
