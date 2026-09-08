using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Persistence;

public sealed record RegistrationPaymentAttemptClaim(PaymentAttempt Attempt, CheckoutDispatchEffect DispatchEffect);

public sealed record RegistrationPaymentAttemptClaimOutcome(PaymentAttempt Attempt, CheckoutDispatchEffect DispatchEffect, bool Created);

public sealed record CheckoutDispatchClaim(
    Guid EffectId,
    Guid TenantId,
    Guid RegistrationOrderId,
    Guid PaymentAttemptId,
    Guid LeaseToken,
    long ProcessingFence,
    CheckoutDispatchReplayKind ReplayKind = CheckoutDispatchReplayKind.None,
    int AttemptCount = 0);

public enum CheckoutDispatchReplayKind
{
    None = 0,
    PreHandoffRetry = 1,
    UnknownRedrive = 2
}

public sealed record PaymentReconciliationClaim(
    Guid TenantId,
    Guid EffectId,
    Guid PaymentAttemptId,
    Guid LeaseToken,
    long ProcessingFence,
    int AttemptCount,
    Guid? CheckoutDispatchEffectId = null,
    DateTime? CheckoutDispatchUnknownAt = null,
    long? CheckoutDispatchProcessingFence = null,
    int? CheckoutDispatchAttemptCount = null);

public enum PaymentReconciliationDisposition
{
    Retry = 1,
    Complete = 2,
    Park = 3
}

public sealed record PaymentReconciliationDecision(
    PaymentReconciliationDisposition Disposition,
    PaymentAttemptStatusEnum Status,
    string? CheckoutSessionId,
    string? PaymentId,
    string? ProviderRequestId,
    string FailureCode,
    DateTime ObservedAt,
    DateTime? NextAttemptAt = null);

public sealed record PaymentReconciliationHealth(
    int Due,
    int Unknown,
    int Parked,
    DateTime? OldestDueAt,
    int ConfigurationBlocked = 0,
    int DuplicateSucceededOrders = 0);

public enum PaymentCancellationDisposition
{
    NoAttempt = 0,
    CancelledBeforeHandoff = 1,
    RequiresReconciliation = 2
}

public enum CheckoutDispatchConfigurationDisposition
{
    Stale = 0,
    Deferred = 1,
    RequiresLifecycleCancellation = 2,
    CancelledExpired = 3
}

public interface IRegistrationPaymentAttemptRepository
{
    Task<(PaymentAttempt Attempt, CheckoutDispatchEffect DispatchEffect)?> GetLatestByOrderAsync(
        Guid tenantId,
        Guid registrationOrderId,
        CancellationToken cancellationToken);

    Task<(PaymentAttempt Attempt, CheckoutDispatchEffect DispatchEffect)?> GetActiveByOrderAsync(
        Guid tenantId,
        Guid registrationOrderId,
        CancellationToken cancellationToken);

    Task<(PaymentAttempt Attempt, CheckoutDispatchEffect DispatchEffect)?> GetByOrderCompositionAsync(
        Guid tenantId,
        Guid registrationOrderId,
        string compositionRevision,
        CancellationToken cancellationToken);

    Task<RegistrationPaymentAttemptClaimOutcome> ClaimAsync(
        RegistrationPaymentAttemptClaim claim,
        CancellationToken cancellationToken);

    Task<bool> ReleaseActiveSlotAsync(PaymentAttempt attempt, DateTime releasedAt, CancellationToken cancellationToken);

    Task<PaymentCancellationDisposition> TryCancelBeforeProviderHandoffAsync(
        Guid tenantId,
        Guid registrationOrderId,
        DateTime cancelledAt,
        CancellationToken cancellationToken);

    Task<PaymentAttempt?> GetByIdForCancellationAsync(
        Guid tenantId,
        Guid paymentAttemptId,
        CancellationToken cancellationToken);

    Task<bool> MarkCancelledAfterProviderAsync(
        Guid tenantId,
        Guid paymentAttemptId,
        Guid actorId,
        DateTime cancelledAt,
        string? providerRequestId,
        CancellationToken cancellationToken);

    Task<bool> RetryParkedPreHandoffAsync(
        Guid tenantId,
        Guid paymentAttemptId,
        DateTime requestedAt,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CheckoutDispatchClaim>> ClaimDueDispatchEffectsAsync(
        string leaseOwner,
        int batchSize,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<PaymentAttempt?> GetClaimedAttemptAsync(
        CheckoutDispatchClaim claim,
        DateTime observedAt,
        CancellationToken cancellationToken);

    Task<bool> MarkCheckoutDispatchPendingAsync(
        CheckoutDispatchClaim claim,
        DateTime dispatchingAt,
        CancellationToken cancellationToken);

    Task<PaymentAttempt?> PrepareCheckoutDispatchAsync(
        CheckoutDispatchClaim claim,
        DateTime preparedAt,
        DateTime minimumCutoff,
        CancellationToken cancellationToken);

    Task<CheckoutDispatchConfigurationDisposition> DeferCheckoutDispatchForConfigurationAsync(
        CheckoutDispatchClaim claim,
        string failureCode,
        DateTime nextAttemptAt,
        DateTime observedAt,
        CancellationToken cancellationToken);

    Task<bool> CancelExpiredConfigurationBlockedAsync(
        CheckoutDispatchClaim claim,
        DateTime observedAt,
        CancellationToken cancellationToken);

    Task<bool> RetryCheckoutDispatchBeforeHandoffAsync(
        CheckoutDispatchClaim claim,
        string failureCode,
        DateTime nextAttemptAt,
        DateTime failedAt,
        CancellationToken cancellationToken);

    Task<bool> CompleteCheckoutDispatchAsync(
        CheckoutDispatchClaim claim,
        string providerCheckoutSessionId,
        string? providerRequestId,
        DateTime completedAt,
        CancellationToken cancellationToken);

    Task<bool> MarkCheckoutDispatchUnknownAsync(
        CheckoutDispatchClaim claim,
        string? providerRequestId,
        DateTime unknownAt,
        CancellationToken cancellationToken);

    Task<bool> FailCheckoutDispatchAsync(
        CheckoutDispatchClaim claim,
        string failureCode,
        string? providerRequestId,
        DateTime failedAt,
        CancellationToken cancellationToken);

    Task<bool> CompleteDispatchAsync(CheckoutDispatchClaim claim, DateTime completedAt, CancellationToken cancellationToken);

    Task<bool> RetryDispatchAsync(CheckoutDispatchClaim claim, DateTime nextAttemptAt, DateTime failedAt, CancellationToken cancellationToken);

    Task<bool> ParkDispatchAsync(CheckoutDispatchClaim claim, string failureCode, DateTime parkedAt, CancellationToken cancellationToken);

    Task<bool> MarkDispatchUnknownAsync(CheckoutDispatchClaim claim, DateTime unknownAt, CancellationToken cancellationToken);

    Task<bool> RequeueUnknownDispatchAsync(
        Guid tenantId,
        Guid effectId,
        DateTime unknownAt,
        long processingFence,
        int attemptCount,
        DateTime nextAttemptAt,
        DateTime resolvedAt,
        CancellationToken cancellationToken);

    Task<bool> RequeueLatestUnknownDispatchAsync(
        Guid tenantId,
        Guid paymentAttemptId,
        DateTime nextAttemptAt,
        DateTime resolvedAt,
        CancellationToken cancellationToken);

    Task<PaymentAttempt?> FindByProviderObjectAsync(
        Guid tenantId,
        string externalAccountId,
        string providerCheckoutSessionId,
        CancellationToken cancellationToken);

    Task EnsureReconciliationDueAsync(
        PaymentAttempt attempt,
        Guid? sourceIncomingWebhookMessageId,
        DateTime dueAt,
        CancellationToken cancellationToken,
        Guid? checkoutDispatchEffectId = null,
        DateTime? checkoutDispatchUnknownAt = null,
        long? checkoutDispatchProcessingFence = null,
        int? checkoutDispatchAttemptCount = null);

    Task<IReadOnlyList<PaymentReconciliationClaim>> ClaimDueReconciliationsAsync(
        string leaseOwner,
        int batchSize,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<PaymentAttempt?> GetReconciliationAttemptAsync(
        PaymentReconciliationClaim claim,
        DateTime observedAt,
        CancellationToken cancellationToken);

    Task<bool> SettleReconciliationAsync(
        PaymentReconciliationClaim claim,
        PaymentReconciliationDecision decision,
        CancellationToken cancellationToken);

    Task<PaymentReconciliationHealth> GetReconciliationHealthAsync(
        DateTime observedAt,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
