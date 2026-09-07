using Explore.Domain;

namespace Explore.Application.Contracts.Waitlist;

public enum FairReturnDispatchOutcome
{
    Succeeded = 1,
    RetryScheduled = 2,
    Unknown = 3,
    Poisoned = 4,
    DeadLettered = 5,
    StaleLease = 6,
}

public enum FairReturnPaymentObservation
{
    Pending = 1,
    Succeeded = 2,
    Failed = 3,
    Unknown = 4,
}

public sealed record FairTenantCursor(
    Guid TenantId,
    DateTime CreatedAt,
    Guid EffectId);

public sealed record FairReturnOrchestrationClaim(
    Guid TenantId,
    Guid EffectId,
    Guid WaitlistPaymentIntentId,
    Guid StableOperationId,
    string ProviderIdempotencyKey,
    long StableCursor,
    long ProcessingFence,
    int AttemptCount,
    int MaximumAttempts,
    bool ExpiredLease);

public sealed record FairReturnOrchestrationDispatchResult(
    FairReturnDispatchOutcome Outcome,
    Guid EffectId,
    string FailureCode);

public sealed record FairReturnOrchestrationDrainResult(
    int Claimed,
    int Succeeded,
    int RetryScheduled,
    int Unknown,
    int Poisoned,
    int DeadLettered,
    int StaleLease)
{
    public int Count(
        FairReturnDispatchOutcome outcome) =>
        outcome switch
        {
            FairReturnDispatchOutcome.Succeeded =>
                Succeeded,
            FairReturnDispatchOutcome
                .RetryScheduled =>
                RetryScheduled,
            FairReturnDispatchOutcome.Unknown =>
                Unknown,
            FairReturnDispatchOutcome.Poisoned =>
                Poisoned,
            FairReturnDispatchOutcome
                .DeadLettered =>
                DeadLettered,
            FairReturnDispatchOutcome.StaleLease =>
                StaleLease,
            _ => 0,
        };
}

public sealed record FairReturnOrchestrationHealth(
    int Pending,
    int Processing,
    int Unknown,
    int DeadLettered,
    DateTime? OldestPendingAt);

public interface IFairReturnOrchestrationRepository
{
    Task<WaitlistPaymentIntent>
        CreatePaymentIntentAsync(
            WaitlistPaymentIntent intent,
            RefundAttempt reservedRefundAttempt,
            FairReturnOrchestrationEffect effect,
            CancellationToken cancellationToken);

    Task<IReadOnlyList<
        FairReturnOrchestrationClaim>> TryClaimDueAsync(
            DateTime claimedAtUtc,
            string leaseOwner,
            Guid? effectId,
            int batchSize,
            int MaximumEffectsPerTenant,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken);

    Task<PaymentAttempt?> GetReplacementPaymentAsync(
        FairReturnOrchestrationClaim claim,
        CancellationToken cancellationToken);

    Task<bool> ObserveReplacementSettlementAsync(
        FairReturnOrchestrationClaim claim,
        DateTime settledAtUtc,
        CancellationToken cancellationToken);

    Task<WaitlistRefundIntent?>
        CreateRefundIntentAsync(
            FairReturnOrchestrationClaim claim,
            DateTime settledAtUtc,
            CancellationToken cancellationToken);

    Task<bool> MarkCompletedAsync(
        FairReturnOrchestrationClaim claim,
        DateTime completedAtUtc,
        CancellationToken cancellationToken);

    Task<FairReturnDispatchOutcome> MarkFailedAsync(
        FairReturnOrchestrationClaim claim,
        string failureCode,
        bool retryable,
        DateTime failedAtUtc,
        DateTime retryAtUtc,
        CancellationToken cancellationToken);

    Task<FairReturnOrchestrationHealth>
        GetHealthAsync(
            DateTime observedAtUtc,
            CancellationToken cancellationToken);
}

public interface IFairReturnOrchestrationDispatcher
{
    Task<FairReturnOrchestrationDispatchResult>
        TryDispatch(
            FairReturnOrchestrationClaim claim,
            CancellationToken cancellationToken);
}
