// ABOUTME: Persistence contract for specialized email dispatch outbox state, attempts, and receipts.
// ABOUTME: Keeps SMTP dispatch state machine in Application while EF Core implementation stays in Persistence.

using Explore.Application.Models;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEmailDispatchOutboxRepository
{
    Task<EmailDispatchOutbox> Create(EmailDispatchOutbox entity, CancellationToken cancellationToken);

    Task<IReadOnlyList<EmailDispatchOutbox>> ClaimPendingBatchAsync(
        EmailDispatchBatchClaimRequest request,
        CancellationToken cancellationToken);

    Task<EmailDispatchOutbox?> TryClaimSpecificAsync(
        EmailDispatchSpecificClaimRequest request,
        CancellationToken cancellationToken);

    Task<EventReminderStateChangeResult> SuppressEventRemindersInCurrentTransactionAsync(
        EventReminderSupersessionRequest request,
        CancellationToken cancellationToken);

    Task<EventReminderStateChangeResult> RescheduleEventRemindersInCurrentTransactionAsync(
        EventReminderRescheduleRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EmailDispatchOutbox>> GetRabbitMqPublishBatch(
        int batchSize,
        DateTime now,
        DateTime retryAttemptsBefore,
        CancellationToken cancellationToken);

    Task<int> CountDueDispatchAsync(
        DateTime now,
        CancellationToken cancellationToken);

    Task<DateTime?> GetOldestDueCreatedAtAsync(
        DateTime now,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, int>> CountDueDispatchByTenantAsync(
        DateTime now,
        int tenantLimit,
        CancellationToken cancellationToken);

    Task<int> CountRetryScheduledAsync(CancellationToken cancellationToken);

    Task<int> CountStaleProcessingAsync(
        DateTime processingStartedBefore,
        CancellationToken cancellationToken);

    Task<int> CountDeadLetteredAsync(CancellationToken cancellationToken);

    Task<int> CountUnknownAsync(CancellationToken cancellationToken);

    Task<int> CountParkedAsync(CancellationToken cancellationToken);

    Task<bool> IsOptionalReminderDeferralActiveAsync(CancellationToken cancellationToken);

    Task<EmailDispatchProcessorState?> GetProcessorState(CancellationToken cancellationToken);

    Task<EmailDispatchProcessorState> SetProcessorPauseState(
        bool isPaused,
        string? pauseReason,
        Guid? changedBy,
        DateTime changedAt,
        CancellationToken cancellationToken);

    Task<EmailDispatchProcessorState> SetGlobalSmtpRateLimitOverride(
        int? rateLimitPerMinute,
        Guid? changedBy,
        DateTime changedAt,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EmailDispatchOutbox>> GetStatusRows(
        Guid tenantId,
        int limit,
        CancellationToken cancellationToken);

    Task<EmailDispatchOutbox?> GetByTenantAndId(
        Guid tenantId,
        Guid outboxId,
        CancellationToken cancellationToken);

    Task<EmailDispatchOutbox?> GetByTenantAndPublishEventId(
        Guid tenantId,
        Guid publishEventId,
        CancellationToken cancellationToken);

    Task<bool> IsTenantPaused(Guid tenantId, CancellationToken cancellationToken);

    Task<EmailDispatchTenantControl?> GetTenantControl(Guid tenantId, CancellationToken cancellationToken);

    Task<EmailDispatchTenantControl> SetTenantPauseState(
        Guid tenantId,
        bool isPaused,
        string? pauseReason,
        Guid? changedBy,
        DateTime changedAt,
        CancellationToken cancellationToken);

    Task<bool> TryParkForOperator(
        Guid tenantId,
        Guid outboxId,
        string reason,
        Guid? changedBy,
        DateTime parkedAt,
        CancellationToken cancellationToken);

    Task<bool> TryReplayForOperator(
        Guid tenantId,
        Guid outboxId,
        Guid? changedBy,
        DateTime replayAt,
        CancellationToken cancellationToken);

    Task<bool> TryResolveWithoutReplay(
        Guid tenantId,
        Guid outboxId,
        string reason,
        Guid? changedBy,
        DateTime resolvedAt,
        CancellationToken cancellationToken);

    Task<bool> TryReconcileUnknown(
        Guid tenantId,
        Guid outboxId,
        EmailDispatchUnknownReconciliationOutcome outcome,
        string reason,
        string? providerMessageId,
        Guid? changedBy,
        DateTime reconciledAt,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetRetentionTenantIds(
        DateTime cutoffUtc,
        int maxTenants,
        CancellationToken cancellationToken);

    Task<int> CountRetentionRedactionEligible(
        Guid tenantId,
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken);

    Task<int> RedactRetentionEligible(
        Guid tenantId,
        DateTime cutoffUtc,
        DateTime redactedAt,
        int batchSize,
        CancellationToken cancellationToken);

    Task<int> SuppressAndRedactTenant(
        Guid tenantId,
        Guid? changedBy,
        DateTime redactedAt,
        CancellationToken cancellationToken);

    Task<EmailDispatchStaleRecoveryResult> RecoverStaleProcessing(
        EmailDispatchStaleRecoveryRequest request,
        CancellationToken cancellationToken);

    Task<EmailDispatchPreHandoffReleaseOutcome> ReleaseClaimBeforeProviderHandoff(
        EmailDispatchPreHandoffRelease request,
        CancellationToken cancellationToken);

    Task MarkRabbitMqPublishSucceeded(
        Guid id,
        DateTime publishedAt,
        CancellationToken cancellationToken);

    Task MarkRabbitMqPublishFailed(
        Guid id,
        string failureCategory,
        DateTime attemptedAt,
        CancellationToken cancellationToken);

    Task SettleProviderAccepted(
        EmailDispatchAcceptedSettlement settlement,
        CancellationToken cancellationToken);

    Task<EmailDispatchFailureSettlementOutcome> SettleProviderFailure(
        EmailDispatchFailureSettlement settlement,
        CancellationToken cancellationToken);

    Task<EmailDispatchAcceptedReconciliationOutcome> ReconcileProviderAccepted(
        EmailDispatchAcceptedSettlement settlement,
        CancellationToken cancellationToken);

}

public sealed record EmailDispatchBatchClaimRequest(
    Guid LeaseToken,
    int BatchSize,
    int MaxRowsPerTenant,
    int GlobalProcessingLimit,
    int TenantProcessingLimit,
    int OptionalReminderBacklogHighWatermark,
    int OptionalReminderBacklogLowWatermark,
    DateTime ClaimedAt);

public sealed record EmailDispatchSpecificClaimRequest(
    Guid TenantId,
    Guid PublishEventId,
    Guid LeaseToken,
    int GlobalProcessingLimit,
    int TenantProcessingLimit,
    int OptionalReminderBacklogHighWatermark,
    int OptionalReminderBacklogLowWatermark,
    DateTime ClaimedAt);

public sealed record EventReminderSupersessionRequest(
    Guid TenantId,
    Guid EventId,
    Guid? RegistrationOrderId,
    Guid? SessionId,
    DateTime SupersededAt,
    string ReasonCode);

public sealed record EventReminderRescheduleRequest(
    Guid TenantId,
    Guid EventId,
    Guid? RegistrationOrderId,
    Guid? SessionId,
    string EventTitle,
    TimeSpan LeadTime,
    DateTime ChangedAt,
    string EventTimeZoneId = "UTC");

public sealed record EventReminderStateChangeResult(
    int OutboxRowsChanged,
    int EmailDeliveryRowsChanged,
    int NotificationsChanged,
    int InAppDeliveryRowsChanged);

public sealed record EmailDispatchAcceptedSettlement(
    Guid TenantId,
    Guid OutboxId,
    Guid ProcessingLeaseToken,
    int AttemptNumber,
    DateTime SettledAt,
    string? ProviderMessageId);

public sealed record EmailDispatchFailureSettlement(
    Guid TenantId,
    Guid OutboxId,
    Guid ProcessingLeaseToken,
    int AttemptNumber,
    SmtpDeliveryOutcome Outcome,
    TimeSpan RetryDelay,
    int MaxAttempts,
    DateTime SettledAt)
{
    public string FailureCategory => Failure.Category;
    public string FailureMessage => Failure.Message;

    private (string Category, string Message) Failure => Outcome switch
    {
        SmtpDeliveryOutcome.TransientFailure => (
            "smtp_send_failed", "SMTP send failed before provider acceptance was confirmed."),
        SmtpDeliveryOutcome.ConfigurationFailure => (
            "smtp_configuration_unavailable", "SMTP delivery is parked until transport configuration is restored."),
        _ => throw new ArgumentOutOfRangeException(nameof(Outcome), Outcome, "Only definite SMTP failures can be settled.")
    };
}

public sealed record EmailDispatchPreHandoffRelease(
    Guid TenantId,
    Guid OutboxId,
    Guid ProcessingLeaseToken,
    int AttemptNumber,
    DateTime ReleasedAt,
    string FailureCategory,
    string FailureMessage);

public sealed record EmailDispatchStaleRecoveryRequest(
    DateTime ProcessingStartedBefore,
    DateTime RecoveredAt,
    string RetryFailureCategory,
    string RetryErrorMessage,
    string UnknownFailureCategory,
    string UnknownErrorMessage,
    int BatchSize);

public sealed record EmailDispatchStaleRecoveryResult(
    int RetryScheduledCount,
    int UnknownCount)
{
    public int RecoveredCount => RetryScheduledCount + UnknownCount;
}

public enum EmailDispatchAcceptedReconciliationOutcome
{
    Sent = 1,
    Unknown = 2,
    StaleClaim = 3
}

public enum EmailDispatchFailureSettlementOutcome
{
    RetryScheduled = 1,
    DeadLettered = 2,
    StaleClaim = 3,
    Parked = 4
}

public enum EmailDispatchPreHandoffReleaseOutcome
{
    Released = 1,
    ProviderHandoffFenced = 2,
    LostClaim = 3
}

public enum EmailDispatchUnknownReconciliationOutcome
{
    Delivered = 1,
    NotDelivered = 2
}
