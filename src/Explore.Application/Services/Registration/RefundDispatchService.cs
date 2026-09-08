using Explore.Application.Contracts.Payments;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services.Registration;

public sealed class RefundDispatchService(
    IRefundAttemptRepository repository,
    IRefundCreator creator,
    TimeProvider timeProvider,
    IEventAddOnRepository? addOns = null)
{
    public async Task<RefundAttempt?> DispatchAsync(
        Guid tenantId,
        Guid refundAttemptId,
        CancellationToken cancellationToken)
    {
        RefundAttempt? attempt = await repository.GetByIdAsync(tenantId, refundAttemptId, cancellationToken);
        if (attempt is null || attempt.Status is not (RefundAttemptStatusEnum.Requested or RefundAttemptStatusEnum.DispatchPending))
        {
            return attempt;
        }

        DateTime dispatchingAt = NextObservation(attempt);
        attempt.MarkDispatchPending(dispatchingAt, null);
        await repository.SaveChangesAsync(cancellationToken);

        RefundProviderResult result = await creator.CreateAsync(CreateRequest(attempt), cancellationToken);
        DateTime observedAt = NextObservation(attempt);
        if (result.Outcome == RefundProviderOutcome.Observed && result.Observation is not null)
        {
            RefundAttemptEvidence.Apply(attempt, result.Observation, observedAt, result.ProviderRequestId);
        }
        else if (result.Outcome == RefundProviderOutcome.Unknown && result.Failure?.ProviderHandoffStarted == true)
        {
            attempt.MarkUnknown(observedAt, result.ProviderRequestId);
        }
        else if (result.Outcome == RefundProviderOutcome.Failed && result.Failure is not null)
        {
            attempt.MarkProviderBlocked(observedAt, result.ProviderRequestId, result.Failure.Code);
        }
        else
        {
            attempt.MarkDispatchPending(observedAt, result.ProviderRequestId);
        }

        await repository.SaveChangesAsync(cancellationToken);
        await ResolveAddOnAllocationAsync(attempt, cancellationToken);
        return attempt;
    }

    internal static RefundCreateRequest CreateRequest(RefundAttempt attempt) => RefundCreateRequest.Create(
        attempt.Id,
        attempt.ProviderCode,
        attempt.ExternalAccountId,
        attempt.ProviderPaymentId,
        attempt.ProviderIdempotencyKey,
        attempt.Allocation.TotalMinor,
        attempt.CurrencyCode,
        checked(attempt.Allocation.PlatformFeeMinor + attempt.Allocation.PlatformContributionMinor));

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
