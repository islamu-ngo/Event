using Explore.Application.Contracts.Persistence;
using Explore.Application.Services;
using Explore.Application.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.NotificationFanout;

public sealed record NotificationFanoutProcessorRoundResult(
    int ClaimedCount,
    int LeaseContentionCount,
    int CapacityDeferredCount,
    int UnavailableCount,
    int CompletedCount,
    int StaleClaimCount,
    int FailedCount,
    int RecipientsProcessed,
    int NotificationsCreated);

public sealed class NotificationFanoutProcessor(
    IServiceScopeFactory scopeFactory,
    IOptions<NotificationFanoutProcessorSettings> options,
    TimeProvider timeProvider,
    BusinessMetrics metrics,
    ILogger<NotificationFanoutProcessor> logger) : BackgroundService
{
    private readonly NotificationFanoutProcessorSettings _settings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            logger.LogInformation("Notification fanout processor is disabled");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessRoundAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Notification fanout processor round failed");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_settings.PollingIntervalSeconds),
                timeProvider,
                stoppingToken);
        }
    }

    public async Task<NotificationFanoutProcessorRoundResult> ProcessRoundAsync(
        CancellationToken cancellationToken)
    {
        DateTime claimedAt = UtcNow();
        NotificationFanoutClaimRoundResult claimRound;
        await using (AsyncServiceScope claimScope = scopeFactory.CreateAsyncScope())
        {
            var repository = claimScope.ServiceProvider.GetRequiredService<INotificationFanoutRunRepository>();
            claimRound = await repository.ClaimDueRoundAsync(
                new NotificationFanoutClaimRoundRequest(
                    _settings.ConsumerId,
                    claimedAt,
                    TimeSpan.FromSeconds(_settings.ClaimLeaseSeconds),
                    _settings.MaxClaimsPerRound,
                    _settings.MaxActiveClaims,
                    _settings.MaxActiveClaimsPerTenant,
                    _settings.OptionalReminderBacklogHighWatermark,
                    _settings.OptionalReminderBacklogLowWatermark),
                cancellationToken);
        }

        metrics.RecordNotificationFanoutProcessorClaims(claimRound.Claims.Count, "claimed");
        metrics.RecordNotificationFanoutProcessorClaims(
            claimRound.LeaseContentionCount,
            "lease_contention");
        metrics.RecordNotificationFanoutProcessorClaims(
            claimRound.CapacityDeferredCount,
            "capacity_deferred");
        metrics.RecordNotificationFanoutProcessorClaims(claimRound.UnavailableCount, "unavailable");

        ClaimExecutionResult[] results = await Task.WhenAll(
            claimRound.Claims.Select(claim => ProcessClaimAsync(claim, cancellationToken)));
        int executionUnavailableCount = results.Count(
            result => !result.Failed
                && result.Outcome == NotificationFanoutPageProcessingOutcome.Unavailable);
        var roundResult = new NotificationFanoutProcessorRoundResult(
            claimRound.Claims.Count,
            claimRound.LeaseContentionCount,
            claimRound.CapacityDeferredCount,
            claimRound.UnavailableCount + executionUnavailableCount,
            results.Count(result => result.Outcome == NotificationFanoutPageProcessingOutcome.Completed),
            results.Count(result => result.Outcome == NotificationFanoutPageProcessingOutcome.StaleClaim),
            results.Count(result => result.Failed),
            results.Sum(result => result.RecipientsProcessed),
            results.Sum(result => result.NotificationsCreated));

        metrics.RecordNotificationFanoutProcessorClaims(roundResult.CompletedCount, "completed");
        metrics.RecordNotificationFanoutProcessorClaims(roundResult.StaleClaimCount, "stale_claim");
        metrics.RecordNotificationFanoutProcessorClaims(roundResult.FailedCount, "failed");
        metrics.RecordNotificationFanoutProcessorClaims(executionUnavailableCount, "unavailable");
        metrics.RecordNotificationFanoutProcessorRecipients(roundResult.RecipientsProcessed, "processed");
        metrics.RecordNotificationFanoutProcessorRecipients(
            roundResult.NotificationsCreated,
            "notification_created");

        await RecordSnapshotAsync(cancellationToken);
        return roundResult;
    }

    private async Task<ClaimExecutionResult> ProcessClaimAsync(
        NotificationFanoutClaim claim,
        CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            var pageProcessor = scope.ServiceProvider.GetRequiredService<NotificationFanoutPageProcessor>();
            NotificationFanoutPageProcessingResult result = await pageProcessor.ProcessAsync(
                claim,
                _settings.PageSize,
                TimeSpan.FromSeconds(_settings.ClaimLeaseSeconds),
                cancellationToken);
            return new ClaimExecutionResult(
                result.Outcome,
                false,
                result.RecipientsMaterialized,
                result.NotificationsCreated);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Notification fanout claim processing failed");
            return ClaimExecutionResult.Failure;
        }
    }

    private async Task RecordSnapshotAsync(CancellationToken cancellationToken)
    {
        DateTime observedAt = UtcNow();
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<INotificationFanoutRunRepository>();
        NotificationFanoutProcessorSnapshot snapshot = await repository.GetProcessorSnapshotAsync(
            observedAt,
            cancellationToken);
        long oldestDueAgeSeconds = snapshot.OldestDueAt.HasValue
            ? Math.Max(0L, (long)(observedAt - snapshot.OldestDueAt.Value).TotalSeconds)
            : 0L;
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
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private sealed record ClaimExecutionResult(
        NotificationFanoutPageProcessingOutcome Outcome,
        bool Failed,
        int RecipientsProcessed,
        int NotificationsCreated)
    {
        public static ClaimExecutionResult Failure { get; } = new(
            NotificationFanoutPageProcessingOutcome.Unavailable,
            true,
            0,
            0);
    }
}
