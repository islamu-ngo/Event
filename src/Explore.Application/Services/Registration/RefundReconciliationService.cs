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

        if (attempt.Status is RefundAttemptStatusEnum.Requested or
            RefundAttemptStatusEnum.DispatchPending ||
            attempt.Status == RefundAttemptStatusEnum.RequiresAction &&
            attempt.FailureCode is not null)
        {
            return attempt;
        }

        RefundProviderResult result = attempt.ProviderRefundId is null
            ? await creator.CreateAsync(RefundDispatchService.CreateRequest(attempt), cancellationToken)
            : await retriever.RetrieveAsync(
                RefundRetrieveRequest.Create(
                    attempt.ProviderCode,
                    attempt.ExternalAccountId,
                    attempt.ProviderPaymentId,
                    attempt.ProviderRefundId,
                    attempt.ProviderIdempotencyKey,
                    attempt.Allocation.TotalMinor,
                    attempt.CurrencyCode,
                    checked(attempt.Allocation.PlatformFeeMinor + attempt.Allocation.PlatformContributionMinor)),
                cancellationToken);
        DateTime observedAt = NextObservation(attempt);
        if (result.Outcome == RefundProviderOutcome.Observed && result.Observation is not null)
        {
            RefundAttemptEvidence.Apply(attempt, result.Observation, observedAt, result.ProviderRequestId);
        }
        else if (result.Outcome == RefundProviderOutcome.Failed && result.Failure is not null)
        {
            attempt.MarkProviderBlocked(observedAt, result.ProviderRequestId, result.Failure.Code);
        }
        else
        {
            attempt.MarkUnknown(observedAt, result.ProviderRequestId);
        }

        await repository.SaveChangesAsync(cancellationToken);
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
