using Explore.Domain;
using Explore.Application.Contracts.Payments;

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
    CapacityExceeded,
    OperationConflict
}

public sealed record RefundReservationResult(
    RefundReservationDisposition Disposition,
    RefundAttempt? Attempt);

public interface IRefundAttemptRepository
{
    Task<RefundAttempt?> TryBeginDispatchAsync(
        RefundAttempt expected, Guid dispatchToken, bool applicationFee, DateTime committedAt,
        DateTime mutationExpiresAt, OutboxMessage recoveryTrigger, CancellationToken cancellationToken);

    Task<RefundAttempt?> SettleAndScheduleAsync(
        RefundAttempt expected, Guid? dispatchToken, RefundProviderResult result, DateTime observedAt,
        OutboxMessage recoveryTrigger, CancellationToken cancellationToken);

    Task<RefundReservationResult?> RecoverOperationAsync(
        Guid tenantId,
        Guid paymentAttemptId,
        string nativeOperationKey,
        string nativeInputDigest,
        CancellationToken cancellationToken);

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

    Task<PaymentDispute> ObserveDisputeAsync(
        PaymentDispute dispute,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
