// ABOUTME: Application boundary for draining durable EmailDispatchOutbox work.
// ABOUTME: Lets hosted services and future schedulers trigger dispatch without owning email state transitions.

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

public sealed record EmailDispatchDrainResult
{
    public int PendingCount { get; init; }
    public int ProcessedCount { get; init; }
    public int SentCount { get; init; }
    public int RetryScheduledCount { get; init; }
    public int DeadLetteredCount { get; init; }
    public int UnknownCount { get; init; }
    public int SkippedCount { get; init; }
    public int TenantPausedCount { get; init; }
    public int AlreadyClaimedCount { get; init; }
    public int ParkedCount { get; init; }
}

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
        or EmailDispatchDrainOutcome.Deferred
        or EmailDispatchDrainOutcome.Parked;
}

public enum EmailDispatchDrainOutcome
{
    Unspecified = 0,
    Sent,
    RetryScheduled,
    DeadLettered,
    Unknown,
    Skipped,
    TenantPaused,
    AlreadyClaimed,
    Missing,
    AlreadySettled,
    Deferred,
    Parked
}
