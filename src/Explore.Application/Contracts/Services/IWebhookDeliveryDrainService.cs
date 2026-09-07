using Explore.Domain;

namespace Explore.Application.Contracts.Services;

public interface IWebhookDeliveryDrainService
{
    Task<WebhookDeliveryDrainResult> ProcessBatchAsync(CancellationToken cancellationToken);

    Task<WebhookDeliveryRecoveryResult> RecoverStaleProcessingAsync(CancellationToken cancellationToken);

    Task<WebhookDeliverySingleDrainResult> ScheduleManualRetryAsync(
        Guid tenantId,
        Guid attemptId,
        WebhookAuditPrincipalKind principalKind,
        string principalReference,
        CancellationToken cancellationToken);
}

public sealed record WebhookDeliveryDrainResult(
    int PendingCount,
    int ProcessedCount,
    int SucceededCount,
    int RetryScheduledCount,
    int AbandonedCount,
    int SkippedCount,
    int AlreadyClaimedCount);

public sealed record WebhookDeliveryRecoveryResult(
    int RecoveredCount,
    DateTimeOffset RecoveryCutoffUtc);

public sealed record WebhookDeliverySingleDrainResult(
    WebhookDeliveryDrainOutcome Outcome,
    Guid? AttemptId = null);

public enum WebhookDeliveryDrainOutcome
{
    Missing = 0,
    Succeeded = 1,
    RetryScheduled = 2,
    Abandoned = 3,
    AlreadyClaimed = 4,
    AlreadySettled = 5,
    Deferred = 6,
    Skipped = 7
}
