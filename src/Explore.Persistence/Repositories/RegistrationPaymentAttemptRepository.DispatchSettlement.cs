using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class RegistrationPaymentAttemptRepository
{
    public Task<bool> CompleteCheckoutDispatchAsync(
        CheckoutDispatchClaim claim,
        string providerCheckoutSessionId,
        string? providerRequestId,
        DateTime completedAt,
        CancellationToken cancellationToken) =>
        SettleCheckoutDispatchAsync(
            claim,
            completedAt,
            query => query.ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.Status, OutboxMessageStatus.Completed)
                .SetProperty(value => value.CompletedAt, completedAt)
                .SetProperty(value => value.ProcessingLeaseOwner, (string?)null)
                .SetProperty(value => value.ProcessingLeaseToken, (Guid?)null)
                .SetProperty(value => value.ProcessingLeaseExpiresAt, (DateTime?)null)
                .SetProperty(value => value.UpdatedAt, completedAt), cancellationToken),
            attempt => attempt.MarkRequiresAction(providerCheckoutSessionId, completedAt, providerRequestId),
            cancellationToken);

    public Task<bool> MarkCheckoutDispatchUnknownAsync(
        CheckoutDispatchClaim claim,
        string? providerRequestId,
        DateTime unknownAt,
        CancellationToken cancellationToken) =>
        SettleCheckoutDispatchAsync(
            claim,
            unknownAt,
            query => query.ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.Status, OutboxMessageStatus.Unknown)
                .SetProperty(value => value.UnknownAt, unknownAt)
                .SetProperty(value => value.NextAttemptAt, (DateTime?)null)
                .SetProperty(value => value.ProcessingLeaseOwner, (string?)null)
                .SetProperty(value => value.ProcessingLeaseToken, (Guid?)null)
                .SetProperty(value => value.ProcessingLeaseExpiresAt, (DateTime?)null)
                .SetProperty(value => value.UpdatedAt, unknownAt), cancellationToken),
            attempt => attempt.MarkUnknown(unknownAt, providerRequestId),
            cancellationToken);

    public Task<bool> FailCheckoutDispatchAsync(
        CheckoutDispatchClaim claim,
        string failureCode,
        string? providerRequestId,
        DateTime failedAt,
        CancellationToken cancellationToken)
    {
        string normalized = failureCode?.Trim() ?? string.Empty;
        if (normalized.Length is 0 or > CheckoutDispatchEffect.MaxFailureCodeLength || normalized.Any(char.IsControl))
        {
            return Task.FromResult(false);
        }

        return SettleCheckoutDispatchAsync(
            claim,
            failedAt,
            query => query.ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.Status, OutboxMessageStatus.DeadLettered)
                .SetProperty(value => value.ParkedAt, failedAt)
                .SetProperty(value => value.LastFailureCode, normalized)
                .SetProperty(value => value.ProcessingLeaseOwner, (string?)null)
                .SetProperty(value => value.ProcessingLeaseToken, (Guid?)null)
                .SetProperty(value => value.ProcessingLeaseExpiresAt, (DateTime?)null)
                .SetProperty(value => value.UpdatedAt, failedAt), cancellationToken),
            attempt =>
            {
                bool changed = attempt.MarkDispatchFailed(failedAt, providerRequestId);
                if (changed)
                {
                    attempt.TryReleaseActiveSlot(failedAt);
                }

                return changed;
            },
            cancellationToken);
    }

    public async Task<bool> CompleteDispatchAsync(CheckoutDispatchClaim claim, DateTime completedAt, CancellationToken cancellationToken)
    {
        if (completedAt.Kind != DateTimeKind.Utc)
        {
            return false;
        }

        int rows = await ActiveClaim(claim, completedAt).ExecuteUpdateAsync(setters => setters
            .SetProperty(value => value.Status, OutboxMessageStatus.Completed)
            .SetProperty(value => value.CompletedAt, completedAt)
            .SetProperty(value => value.ProcessingLeaseOwner, (string?)null)
            .SetProperty(value => value.ProcessingLeaseToken, (Guid?)null)
            .SetProperty(value => value.ProcessingLeaseExpiresAt, (DateTime?)null)
            .SetProperty(value => value.UpdatedAt, completedAt), cancellationToken);
        return ClearTrackerWhenUpdated(rows);
    }

    public async Task<bool> RetryDispatchAsync(CheckoutDispatchClaim claim, DateTime nextAttemptAt, DateTime failedAt, CancellationToken cancellationToken)
    {
        if (failedAt.Kind != DateTimeKind.Utc || nextAttemptAt.Kind != DateTimeKind.Utc || nextAttemptAt <= failedAt)
        {
            return false;
        }

        int rows = await ActiveClaim(claim, failedAt).ExecuteUpdateAsync(setters => setters
            .SetProperty(value => value.Status, OutboxMessageStatus.Failed)
            .SetProperty(value => value.NextAttemptAt, nextAttemptAt)
            .SetProperty(value => value.ProcessingLeaseOwner, (string?)null)
            .SetProperty(value => value.ProcessingLeaseToken, (Guid?)null)
            .SetProperty(value => value.ProcessingLeaseExpiresAt, (DateTime?)null)
            .SetProperty(value => value.UpdatedAt, failedAt), cancellationToken);
        return ClearTrackerWhenUpdated(rows);
    }

    public async Task<bool> ParkDispatchAsync(CheckoutDispatchClaim claim, string failureCode, DateTime parkedAt, CancellationToken cancellationToken)
    {
        string normalized = failureCode.Trim();
        if (parkedAt.Kind != DateTimeKind.Utc || normalized.Length is 0 or > CheckoutDispatchEffect.MaxFailureCodeLength)
        {
            return false;
        }

        int rows = await ActiveClaim(claim, parkedAt).ExecuteUpdateAsync(setters => setters
            .SetProperty(value => value.Status, OutboxMessageStatus.DeadLettered)
            .SetProperty(value => value.ParkedAt, parkedAt)
            .SetProperty(value => value.LastFailureCode, normalized)
            .SetProperty(value => value.ProcessingLeaseOwner, (string?)null)
            .SetProperty(value => value.ProcessingLeaseToken, (Guid?)null)
            .SetProperty(value => value.ProcessingLeaseExpiresAt, (DateTime?)null)
            .SetProperty(value => value.UpdatedAt, parkedAt), cancellationToken);
        return ClearTrackerWhenUpdated(rows);
    }

    public async Task<bool> MarkDispatchUnknownAsync(CheckoutDispatchClaim claim, DateTime unknownAt, CancellationToken cancellationToken)
    {
        if (unknownAt.Kind != DateTimeKind.Utc)
        {
            return false;
        }

        int rows = await ActiveClaim(claim, unknownAt).ExecuteUpdateAsync(setters => setters
            .SetProperty(value => value.Status, OutboxMessageStatus.Unknown)
            .SetProperty(value => value.UnknownAt, unknownAt)
            .SetProperty(value => value.NextAttemptAt, (DateTime?)null)
            .SetProperty(value => value.ProcessingLeaseOwner, (string?)null)
            .SetProperty(value => value.ProcessingLeaseToken, (Guid?)null)
            .SetProperty(value => value.ProcessingLeaseExpiresAt, (DateTime?)null)
            .SetProperty(value => value.UpdatedAt, unknownAt), cancellationToken);
        return ClearTrackerWhenUpdated(rows);
    }

    public async Task<bool> RequeueUnknownDispatchAsync(
        Guid tenantId,
        Guid effectId,
        DateTime unknownAt,
        long processingFence,
        int attemptCount,
        DateTime nextAttemptAt,
        DateTime resolvedAt,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || effectId == Guid.Empty || unknownAt.Kind != DateTimeKind.Utc || processingFence < 0 || attemptCount < 0 ||
            nextAttemptAt.Kind != DateTimeKind.Utc || resolvedAt.Kind != DateTimeKind.Utc || nextAttemptAt < resolvedAt)
        {
            return false;
        }

        int rows = await dbContext.CheckoutDispatchEffects
            .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationFinalizationWorkerCrossTenantQueue)
            .Where(value =>
                value.TenantId == tenantId && value.Id == effectId && value.Status == OutboxMessageStatus.Unknown &&
                value.UnknownAt == unknownAt && value.ProcessingFence == processingFence && value.AttemptCount == attemptCount)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.Status, OutboxMessageStatus.Failed)
                .SetProperty(value => value.NextAttemptAt, nextAttemptAt)
                .SetProperty(value => value.ProcessingLeaseOwner, (string?)null)
                .SetProperty(value => value.ProcessingLeaseToken, (Guid?)null)
                .SetProperty(value => value.ProcessingLeaseExpiresAt, (DateTime?)null)
                .SetProperty(value => value.UpdatedAt, resolvedAt), cancellationToken);
        return ClearTrackerWhenUpdated(rows);
    }

    public async Task<bool> RequeueLatestUnknownDispatchAsync(
        Guid tenantId,
        Guid paymentAttemptId,
        DateTime nextAttemptAt,
        DateTime resolvedAt,
        CancellationToken cancellationToken)
    {
        PaymentReconciliationEffect? reconciliation = await dbContext.PaymentReconciliationEffects
            .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationFinalizationWorkerCrossTenantQueue)
            .AsNoTracking()
            .SingleOrDefaultAsync(value =>
                value.TenantId == tenantId && value.PaymentAttemptId == paymentAttemptId,
                cancellationToken);
        if (reconciliation?.CheckoutDispatchEffectId is not { } dispatchEffectId ||
            reconciliation.CheckoutDispatchUnknownAt is not { } unknownAt ||
            reconciliation.CheckoutDispatchProcessingFence is not { } dispatchFence ||
            reconciliation.CheckoutDispatchAttemptCount is not { } dispatchAttemptCount)
        {
            return false;
        }

        return await RequeueUnknownDispatchAsync(
            tenantId,
            dispatchEffectId,
            DateTime.SpecifyKind(unknownAt, DateTimeKind.Utc),
            dispatchFence,
            dispatchAttemptCount,
            nextAttemptAt,
            resolvedAt,
            cancellationToken);
    }

    private IQueryable<CheckoutDispatchEffect> ActiveClaim(CheckoutDispatchClaim claim, DateTime observedAt) => dbContext.CheckoutDispatchEffects
        .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationFinalizationWorkerCrossTenantQueue)
        .Where(value =>
            value.TenantId == claim.TenantId && value.Id == claim.EffectId &&
            value.RegistrationOrderId == claim.RegistrationOrderId && value.PaymentAttemptId == claim.PaymentAttemptId &&
            value.Status == OutboxMessageStatus.Processing && value.ProcessingLeaseToken == claim.LeaseToken &&
            value.ProcessingFence == claim.ProcessingFence && value.ProcessingLeaseExpiresAt > observedAt);

    private async Task<bool> SettleCheckoutDispatchAsync(
        CheckoutDispatchClaim claim,
        DateTime observedAt,
        Func<IQueryable<CheckoutDispatchEffect>, Task<int>> settleEffect,
        Func<PaymentAttempt, bool> settleAttempt,
        CancellationToken cancellationToken)
    {
        if (observedAt.Kind != DateTimeKind.Utc)
        {
            return false;
        }

        return await ExecuteFencedTransactionAsync(async token =>
        {
            int rows = await settleEffect(ActiveClaim(claim, observedAt));
            if (rows != 1)
            {
                return false;
            }

            PaymentAttempt attempt = await LoadClaimedAttemptAsync(claim, token)
                ?? throw new InvalidOperationException("Claimed checkout dispatch attempt is missing.");
            _ = settleAttempt(attempt);
            await ClosePendingMaterialChangeChoicesAsync(attempt, observedAt, token);
            if (attempt.ProviderCheckoutSessionId is not null || attempt.PaymentAttemptStatusId == (int)PaymentAttemptStatusEnum.Unknown)
            {
                PaymentReconciliationEffect? reconciliation = await dbContext.PaymentReconciliationEffects
                    .SingleOrDefaultAsync(value => value.TenantId == attempt.TenantId && value.PaymentAttemptId == attempt.Id, token);
                bool isUnknownDispatch = attempt.PaymentAttemptStatusId == (int)PaymentAttemptStatusEnum.Unknown;
                Guid? dispatchEffectId = isUnknownDispatch ? claim.EffectId : null;
                DateTime? unknownAt = isUnknownDispatch ? observedAt : null;
                long? dispatchFence = isUnknownDispatch ? claim.ProcessingFence : null;
                int? dispatchAttemptCount = isUnknownDispatch ? claim.AttemptCount : null;
                if (reconciliation is null)
                {
                    await dbContext.PaymentReconciliationEffects.AddAsync(
                        PaymentReconciliationEffect.Create(
                            attempt,
                            observedAt,
                            checkoutDispatchEffectId: dispatchEffectId,
                            checkoutDispatchUnknownAt: unknownAt,
                            checkoutDispatchProcessingFence: dispatchFence,
                            checkoutDispatchAttemptCount: dispatchAttemptCount),
                        token);
                }
                else
                {
                    reconciliation.MakeDue(
                        observedAt,
                        null,
                        dispatchEffectId,
                        unknownAt,
                        dispatchFence,
                        dispatchAttemptCount);
                }
            }
            await dbContext.SaveChangesAsync(token);
            return true;
        }, cancellationToken);
    }

    private Task<PaymentAttempt?> LoadClaimedAttemptAsync(CheckoutDispatchClaim claim, CancellationToken cancellationToken) =>
        dbContext.PaymentAttempts
            .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationFinalizationWorkerCrossTenantQueue)
            .SingleOrDefaultAsync(
                value => value.TenantId == claim.TenantId && value.Id == claim.PaymentAttemptId && value.RegistrationOrderId == claim.RegistrationOrderId,
                cancellationToken);
}
