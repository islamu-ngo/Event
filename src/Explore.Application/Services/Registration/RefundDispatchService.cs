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
        if (attempt is null || !attempt.CanDispatchBuyer)
        {
            return attempt;
        }

        DateTime dispatchingAt = NextObservation(attempt);
        // Refund mutation retention is independent of the checkout/session cutoff.
        // Unsupported providers have no certified mutation window.
        if (attempt.ProviderCode != "stripe" || attempt.ProviderMutationExpiresAt <= dispatchingAt)
            return await BlockExpiredAsync(attempt, dispatchingAt, cancellationToken);

        Guid token = Guid.CreateVersion7();
        attempt = await repository.TryBeginDispatchAsync(
            attempt, token, applicationFee: false, dispatchingAt, dispatchingAt.AddHours(23),
            RefundOutboxMessageFactory.CreateReconciliation(attempt, dispatchingAt, dispatchingAt.AddMinutes(1)),
            cancellationToken);
        if (attempt is null)
            return await repository.GetByIdAsync(tenantId, refundAttemptId, cancellationToken);

        // Exceptions carry no positive pre-handoff proof. The committed recovery trigger
        // survives even when cancellation prevents this request from writing again.
        RefundProviderResult result = await creator.CreateAsync(CreateRequest(attempt), cancellationToken);
        DateTime observedAt = NextObservation(attempt);
        attempt = await repository.SettleAndScheduleAsync(
            attempt, token, result, observedAt,
            RefundOutboxMessageFactory.CreateReconciliation(attempt, observedAt, observedAt.AddMinutes(1)),
            cancellationToken);
        if (attempt is not null)
            await ResolveAddOnAllocationAsync(attempt, cancellationToken);
        return attempt;
    }

    private Task<RefundAttempt?> BlockExpiredAsync(
        RefundAttempt attempt, DateTime observedAt, CancellationToken cancellationToken) =>
        repository.SettleAndScheduleAsync(
            attempt, null,
            RefundProviderResult.Failed(new("refund_provider_retention_expired", RefundProviderFailureKind.Configuration)),
            observedAt, RefundOutboxMessageFactory.CreateReconciliation(attempt, observedAt, observedAt),
            cancellationToken);

    internal static RefundCreateRequest CreateRequest(RefundAttempt attempt) => RefundCreateRequest.Create(
        attempt.Id,
        attempt.ProviderCode,
        attempt.ExternalAccountId,
        attempt.ProviderPaymentId,
        attempt.ProviderIdempotencyKey,
        attempt.Allocation.TotalMinor,
        attempt.CurrencyCode,
        checked(attempt.Allocation.PlatformFeeMinor + attempt.Allocation.PlatformContributionMinor)) with
    {
        MutationExpiresAt = attempt.ProviderMutationExpiresAt
    };

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
