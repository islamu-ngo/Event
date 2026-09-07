namespace Explore.Application.Contracts.Services;

public interface IEmailDispatchDrainService
{
    Task<EmailDispatchDrainResult> ProcessBatchAsync(CancellationToken cancellationToken);

    Task<EmailDispatchRecoveryResult> RecoverStaleProcessingAsync(CancellationToken cancellationToken);

    Task<EmailDispatchSingleDrainResult> ProcessSingleAsync(
        Guid tenantId,
        Guid publishEventId,
        string consumerId,
        CancellationToken cancellationToken);
}

public sealed record EmailDispatchDrainResult(
    int PendingCount,
    int ProcessedCount,
    int SentCount,
    int RetryScheduledCount,
    int DeadLetteredCount,
    int UnknownCount,
    int SkippedCount,
    int TenantPausedCount,
    int AlreadyClaimedCount);

public sealed record EmailDispatchRecoveryResult(
    int RecoveredCount,
    DateTime ProcessingStartedBefore);

public sealed record EmailDispatchSingleDrainResult(
    EmailDispatchDrainOutcome Outcome,
    Guid? OutboxId = null)
{
    public bool IsDurableOutcome => Outcome is EmailDispatchDrainOutcome.Sent
        or EmailDispatchDrainOutcome.RetryScheduled
        or EmailDispatchDrainOutcome.DeadLettered
        or EmailDispatchDrainOutcome.Unknown
        or EmailDispatchDrainOutcome.Skipped
        or EmailDispatchDrainOutcome.TenantPaused
        or EmailDispatchDrainOutcome.AlreadyClaimed
        or EmailDispatchDrainOutcome.AlreadySettled
        or EmailDispatchDrainOutcome.Deferred;
}

public enum EmailDispatchDrainOutcome
{
    Sent,
    RetryScheduled,
    DeadLettered,
    Unknown,
    Skipped,
    TenantPaused,
    AlreadyClaimed,
    Missing,
    AlreadySettled,
    Deferred
}
