using Explore.Application.Contracts.Payments;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services.Registration;

public sealed class RefundReconciliationService(
    IRefundAttemptRepository repository,
    IRefundCreator creator,
    IRefundRetriever retriever,
    TimeProvider timeProvider,
    IEventAddOnRepository? addOns = null)
{
    public async Task<RefundAttempt?> ReconcileAsync(
        Guid tenantId,
        Guid refundAttemptId,
        CancellationToken cancellationToken)
    {
        RefundAttempt? attempt = await repository.GetByIdAsync(tenantId, refundAttemptId, cancellationToken);
        if (attempt is null)
        {
            return attempt;
        }

        if (attempt.Status is
            RefundAttemptStatusEnum.Succeeded or
            RefundAttemptStatusEnum.Failed or
            RefundAttemptStatusEnum.Cancelled)
        {
            await ResolveAddOnAllocationAsync(attempt, cancellationToken);
            return attempt;
        }

        if (attempt.CanDispatchBuyer)
            return await new RefundDispatchService(repository, creator, timeProvider, addOns)
                .DispatchAsync(tenantId, refundAttemptId, cancellationToken);

        if (attempt.Status == RefundAttemptStatusEnum.RequiresAction &&
            attempt.FailureCode is not null)
        {
            return attempt;
        }

        DateTime startedAt = NextObservation(attempt);
        Guid? dispatchToken = null;
        if (attempt.CanDispatchApplicationFee && attempt.ProviderMutationExpiresAt > startedAt)
        {
            dispatchToken = Guid.CreateVersion7();
            attempt = await repository.TryBeginDispatchAsync(
                attempt, dispatchToken.Value, applicationFee: true, startedAt, attempt.ProviderMutationExpiresAt.Value,
                RefundOutboxMessageFactory.CreateReconciliation(attempt, startedAt, startedAt.AddMinutes(1)),
                cancellationToken);
            if (attempt is null)
                return await repository.GetByIdAsync(tenantId, refundAttemptId, cancellationToken);
        }

        // An ambiguous buyer/fee handoff is recovered by provider observation, never
        // by repeating a mutation merely because its durable row says pending/unknown.
        RefundProviderResult result = await retriever.RetrieveAsync(
                RefundRetrieveRequest.Create(
                    attempt.ProviderCode,
                    attempt.ExternalAccountId,
                    attempt.ProviderPaymentId,
                    attempt.ProviderRefundId ?? string.Empty,
                    attempt.ProviderIdempotencyKey,
                    attempt.Allocation.TotalMinor,
                    attempt.CurrencyCode,
                    checked(attempt.Allocation.PlatformFeeMinor + attempt.Allocation.PlatformContributionMinor)) with
                {
                    RefundAttemptId = attempt.Id,
                    AllowApplicationFeeMutation = dispatchToken.HasValue,
                    MutationExpiresAt = attempt.ProviderMutationExpiresAt
                },
                cancellationToken);
        DateTime observedAt = NextObservation(attempt);
        attempt = await repository.SettleAndScheduleAsync(
            attempt, dispatchToken, result, observedAt,
            RefundOutboxMessageFactory.CreateReconciliation(attempt, observedAt, observedAt.AddMinutes(1)),
            cancellationToken);
        if (attempt is not null)
            await ResolveAddOnAllocationAsync(attempt, cancellationToken);
        return attempt;
    }

    private DateTime NextObservation(RefundAttempt attempt)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        return now >= attempt.LastObservedAt ? now : attempt.LastObservedAt;
    }

    private Task<EventAddOnRefundAllocation?> ResolveAddOnAllocationAsync(
        RefundAttempt attempt,
        CancellationToken cancellationToken) =>
        addOns is null
            ? Task.FromResult<EventAddOnRefundAllocation?>(null)
            : attempt.Status switch
            {
                RefundAttemptStatusEnum.Succeeded =>
                    addOns.ResolveRefundAsync(
                        attempt.TenantId,
                        attempt.Id,
                        providerSucceeded: true,
                        attempt.LastObservedAt,
                        cancellationToken),
                RefundAttemptStatusEnum.Failed or RefundAttemptStatusEnum.Cancelled =>
                    addOns.ResolveRefundAsync(
                        attempt.TenantId,
                        attempt.Id,
                        providerSucceeded: false,
                        attempt.LastObservedAt,
                        cancellationToken),
                _ => Task.FromResult<EventAddOnRefundAllocation?>(null),
            };
}
