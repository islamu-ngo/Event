using Explore.Application.Contracts.Waitlist;
using Explore.Domain.Enums;

namespace Explore.Application.Services.Waitlist;

public sealed class FairReturnOrchestrationService(
    IFairReturnOrchestrationRepository repository,
    TimeProvider timeProvider) :
    IFairReturnOrchestrationDispatcher
{
    public Task<Domain.WaitlistPaymentIntent> StartAsync(
        Domain.WaitlistPaymentIntent intent,
        Domain.RefundAttempt reservedRefundAttempt,
        Domain.FairReturnOrchestrationEffect effect,
        CancellationToken cancellationToken) =>
        repository.CreatePaymentIntentAsync(
            intent,
            reservedRefundAttempt,
            effect,
            cancellationToken);

    public async Task<FairReturnOrchestrationDispatchResult>
        TryDispatch(
            FairReturnOrchestrationClaim claim,
            CancellationToken cancellationToken)
    {
        DateTime observedAt =
            timeProvider.GetUtcNow().UtcDateTime;
        if (claim.StableOperationId == Guid.Empty
            || string.IsNullOrWhiteSpace(
                claim.ProviderIdempotencyKey))
        {
            return await FailAsync(
                claim,
                FairReturnDispatchOutcome.Poisoned,
                "INVALID_STABLE_IDEMPOTENCY",
                retryable: false,
                observedAt,
                cancellationToken);
        }

        Domain.PaymentAttempt? payment =
            await repository
                .GetReplacementPaymentAsync(
                    claim,
                    cancellationToken);
        if (payment is null)
        {
            return await FailAsync(
                claim,
                FairReturnDispatchOutcome.Poisoned,
                "PAYMENT_POINTER_MISSING",
                retryable: false,
                observedAt,
                cancellationToken);
        }

        PaymentAttemptStatusEnum status =
            (PaymentAttemptStatusEnum)
                payment.PaymentAttemptStatusId;
        if (status == PaymentAttemptStatusEnum.Succeeded)
        {
            await repository
                .ObserveReplacementSettlementAsync(
                    claim,
                    observedAt,
                    cancellationToken);
            Domain.WaitlistRefundIntent? refund =
                await repository
                    .CreateRefundIntentAsync(
                        claim,
                        observedAt,
                        cancellationToken);
            if (refund is null)
            {
                return await FailAsync(
                    claim,
                    FairReturnDispatchOutcome.Poisoned,
                    "REFUND_INTENT_UNAVAILABLE",
                    retryable: false,
                    observedAt,
                    cancellationToken);
            }
            bool completed =
                await repository.MarkCompletedAsync(
                    claim,
                    observedAt,
                    cancellationToken);
            return new FairReturnOrchestrationDispatchResult(
                completed
                    ? FairReturnDispatchOutcome.Succeeded
                    : FairReturnDispatchOutcome.StaleLease,
                claim.EffectId,
                completed
                    ? string.Empty
                    : "STALE_LEASE");
        }

        if (status is PaymentAttemptStatusEnum.Failed
            or PaymentAttemptStatusEnum.Cancelled)
        {
            return await FailAsync(
                claim,
                FairReturnDispatchOutcome.Poisoned,
                "REPLACEMENT_PAYMENT_TERMINAL",
                retryable: false,
                observedAt,
                cancellationToken);
        }

        return await FailAsync(
            claim,
            FairReturnDispatchOutcome.Unknown,
            "REPLACEMENT_PAYMENT_UNKNOWN",
            retryable: true,
            observedAt,
            cancellationToken);
    }

    private async Task<
        FairReturnOrchestrationDispatchResult> FailAsync(
            FairReturnOrchestrationClaim claim,
            FairReturnDispatchOutcome requestedOutcome,
            string failureCode,
            bool retryable,
            DateTime failedAt,
            CancellationToken cancellationToken)
    {
        int exponent = Math.Min(
            Math.Max(claim.AttemptCount - 1, 0),
            10);
        DateTime retryAt = failedAt.AddSeconds(
            Math.Min(3600, 5 * (1 << exponent)));
        FairReturnDispatchOutcome transition =
            await repository.MarkFailedAsync(
                claim,
                failureCode,
                retryable,
                failedAt,
                retryAt,
                cancellationToken);
        FairReturnDispatchOutcome outcome =
            transition switch
            {
                FairReturnDispatchOutcome
                    .DeadLettered =>
                    FairReturnDispatchOutcome
                        .DeadLettered,
                FairReturnDispatchOutcome
                    .StaleLease =>
                    FairReturnDispatchOutcome.StaleLease,
                _ => requestedOutcome,
            };
        return new FairReturnOrchestrationDispatchResult(
            outcome,
            claim.EffectId,
            failureCode);
    }
}
