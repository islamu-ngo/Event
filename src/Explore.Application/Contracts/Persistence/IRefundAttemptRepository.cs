using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public enum RefundReservationDisposition
{
    Reserved,
    Duplicate,
    PaymentNotFound,
    PaymentNotCaptured,
    AuthorityMismatch,
    MaterialChangeChoiceConflict,
    OpenDispute,
    CapacityExceeded
}

public sealed record RefundReservationResult(
    RefundReservationDisposition Disposition,
    RefundAttempt? Attempt);

public sealed record RefundReconciliationHealth(
    int Pending,
    int Unknown,
    int RequiresAction,
    int Failed,
    int CampaignsRequiringOperator,
    int DisputesDueSoon,
    int DisputesDueWithin72Hours,
    int DisputesOverdue,
    DateTime? OldestNonTerminalAt);

public interface IRefundAttemptRepository
{
    Task<RefundReservationResult> ReserveAsync(
        RefundAttempt attempt,
        CancellationToken cancellationToken);

    Task<RefundReservationResult> ReserveAndScheduleAsync(
        RefundAttempt attempt,
        OutboxMessage dispatchTrigger,
        CancellationToken cancellationToken);

    Task<RefundReservationResult> ReserveAndRecordMaterialChangeRefundAsync(
        RefundAttempt attempt,
        Guid materialChangeChoiceId,
        Guid actorId,
        DateTime decidedAt,
        OutboxMessage dispatchTrigger,
        CancellationToken cancellationToken);

    Task<RefundAttempt?> GetByIdAsync(
        Guid tenantId,
        Guid refundAttemptId,
        CancellationToken cancellationToken);

    Task<bool> RetryProviderBlockedAndScheduleAsync(
        RefundAttempt attempt,
        OutboxMessage reconciliationTrigger,
        DateTime requestedAt,
        CancellationToken cancellationToken);

    Task<long> GetRefundableCapacityAsync(
        Guid tenantId,
        Guid paymentAttemptId,
        CancellationToken cancellationToken);

    Task<PaymentAttempt?> FindPaymentByProviderPaymentAsync(
        Guid tenantId,
        string externalAccountId,
        string providerPaymentId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PaymentDispute>> GetDisputesAsync(
        Guid tenantId,
        Guid paymentAttemptId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RefundAttempt>> GetByPaymentAsync(
        Guid tenantId,
        Guid paymentAttemptId,
        CancellationToken cancellationToken);

    Task<PaidOrderAcceptanceSnapshot?> GetAcceptanceAsync(
        Guid tenantId,
        Guid paidOrderAcceptanceSnapshotId,
        CancellationToken cancellationToken);

    Task<RefundReconciliationHealth> GetReconciliationHealthAsync(
        DateTime observedAt,
        CancellationToken cancellationToken);

    Task<PaymentDispute> ObserveDisputeAsync(
        PaymentDispute dispute,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
