using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class RegistrationPaymentAttemptRepository
{
    public async Task<IReadOnlyList<CheckoutDispatchClaim>> ClaimDueDispatchEffectsAsync(
        string leaseOwner,
        int batchSize,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(leaseOwner) || leaseOwner.Trim().Length > CheckoutDispatchEffect.MaxLeaseOwnerLength ||
            batchSize is < 1 or > 1000 || leaseDuration <= TimeSpan.Zero || claimedAt.Kind != DateTimeKind.Utc)
        {
            return [];
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            string owner = leaseOwner.Trim();
            List<CheckoutDispatchCandidate> candidates = await dbContext.CheckoutDispatchEffects
                .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationFinalizationWorkerCrossTenantQueue)
                .AsNoTracking()
                .Where(value =>
                    ((value.Status == OutboxMessageStatus.Pending || value.Status == OutboxMessageStatus.Failed) &&
                      (value.NextAttemptAt == null || value.NextAttemptAt <= claimedAt)) ||
                    (value.Status == OutboxMessageStatus.Processing && value.ProcessingLeaseExpiresAt <= claimedAt))
                .OrderBy(value => value.NextAttemptAt ?? value.CreatedAt)
                .ThenBy(value => value.Id)
                .Select(value => new CheckoutDispatchCandidate(
                    value.Id,
                    value.TenantId,
                    value.RegistrationOrderId,
                    value.PaymentAttemptId,
                    value.Status,
                    value.NextAttemptAt,
                    value.ProcessingLeaseToken,
                    value.ProcessingLeaseExpiresAt,
                    value.ProcessingFence,
                    value.AttemptCount,
                    value.UnknownAt,
                    value.LastFailureCode))
                .Take(4)
                .ToListAsync(cancellationToken);

            List<CheckoutDispatchClaim> claims = new(Math.Min(batchSize, candidates.Count));
            foreach (CheckoutDispatchCandidate candidate in candidates)
            {
                if (claims.Count == 1)
                {
                    break;
                }

                Guid leaseToken = Guid.CreateVersion7();
                long nextFence = checked(candidate.ProcessingFence + 1);
                int rows = await ClaimCandidate(candidate, owner, leaseToken, claimedAt, leaseDuration, nextFence, cancellationToken);
                if (rows == 1)
                {
                    CheckoutDispatchReplayKind replayKind = candidate.Status == OutboxMessageStatus.Failed && candidate.UnknownAt.HasValue
                        ? CheckoutDispatchReplayKind.UnknownRedrive
                        : candidate.Status == OutboxMessageStatus.Failed && candidate.LastFailureCode is not null
                            ? CheckoutDispatchReplayKind.PreHandoffRetry
                            : CheckoutDispatchReplayKind.None;
                    claims.Add(new(
                        candidate.EffectId,
                        candidate.TenantId,
                        candidate.RegistrationOrderId,
                        candidate.PaymentAttemptId,
                        leaseToken,
                        nextFence,
                        replayKind,
                        checked(candidate.AttemptCount + 1)));
                }
            }

            return claims;
        });
    }

    public async Task<PaymentAttempt?> GetClaimedAttemptAsync(
        CheckoutDispatchClaim claim,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        if (observedAt.Kind != DateTimeKind.Utc)
        {
            return null;
        }

        return await (from effect in ActiveClaim(claim, observedAt).AsNoTracking()
                      join attempt in dbContext.PaymentAttempts
                              .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationFinalizationWorkerCrossTenantQueue)
                              .Include(value => value.AcceptanceSnapshot)
                              .AsNoTracking()
                          on new { effect.TenantId, Id = effect.PaymentAttemptId } equals new { attempt.TenantId, attempt.Id }
                      select attempt).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> MarkCheckoutDispatchPendingAsync(
        CheckoutDispatchClaim claim,
        DateTime dispatchingAt,
        CancellationToken cancellationToken)
    {
        if (dispatchingAt.Kind != DateTimeKind.Utc)
        {
            return false;
        }

        return await ExecuteFencedTransactionAsync(async token =>
        {
            int rows = await ActiveClaim(claim, dispatchingAt).ExecuteUpdateAsync(
                setters => setters.SetProperty(value => value.UpdatedAt, dispatchingAt),
                token);
            if (rows != 1)
            {
                return false;
            }

            PaymentAttempt attempt = await LoadClaimedAttemptAsync(claim, token)
                ?? throw new InvalidOperationException("Claimed checkout dispatch attempt is missing.");
            RegistrationOrder? order = await dbContext.RegistrationOrders
                .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationFinalizationWorkerCrossTenantQueue)
                .AsNoTracking()
                .SingleOrDefaultAsync(value => value.TenantId == claim.TenantId && value.Id == claim.RegistrationOrderId, token);
            if (!attempt.PaidOrderAcceptanceSnapshotId.HasValue ||
                (order is not null &&
                 ((RegistrationOrderStatusEnum)order.RegistrationOrderStatusId != RegistrationOrderStatusEnum.AwaitingPayment ||
                  order.TotalDueMinorSnapshot <= 0 ||
                  order.ExpiresAt is not { } cutoff || cutoff <= dispatchingAt)) ||
                attempt.ExpiresAt is not { } attemptCutoff || attemptCutoff <= dispatchingAt)
            {
                return false;
            }

            _ = attempt.MarkDispatchPending(dispatchingAt, providerRequestId: null);
            await dbContext.SaveChangesAsync(token);
            return true;
        }, cancellationToken);
    }

    public async Task<bool> RetryCheckoutDispatchBeforeHandoffAsync(
        CheckoutDispatchClaim claim,
        string failureCode,
        DateTime nextAttemptAt,
        DateTime failedAt,
        CancellationToken cancellationToken)
    {
        string normalized = failureCode?.Trim() ?? string.Empty;
        if (normalized.Length is 0 or > CheckoutDispatchEffect.MaxFailureCodeLength || normalized.Any(char.IsControl) ||
            failedAt.Kind != DateTimeKind.Utc || nextAttemptAt.Kind != DateTimeKind.Utc || nextAttemptAt <= failedAt)
        {
            return false;
        }

        int rows = await ActiveClaim(claim, failedAt).ExecuteUpdateAsync(setters => setters
            .SetProperty(value => value.Status, OutboxMessageStatus.Failed)
            .SetProperty(value => value.NextAttemptAt, nextAttemptAt)
            .SetProperty(value => value.LastFailureCode, normalized)
            .SetProperty(value => value.ProcessingLeaseOwner, (string?)null)
            .SetProperty(value => value.ProcessingLeaseToken, (Guid?)null)
            .SetProperty(value => value.ProcessingLeaseExpiresAt, (DateTime?)null)
            .SetProperty(value => value.UpdatedAt, failedAt), cancellationToken);
        return ClearTrackerWhenUpdated(rows);
    }

    public Task<PaymentAttempt?> PrepareCheckoutDispatchAsync(
        CheckoutDispatchClaim claim,
        DateTime preparedAt,
        DateTime minimumCutoff,
        CancellationToken cancellationToken) =>
        ExecuteFencedTransactionAsync<PaymentAttempt?>(async token =>
        {
            if (preparedAt.Kind != DateTimeKind.Utc || minimumCutoff.Kind != DateTimeKind.Utc ||
                minimumCutoff <= preparedAt.AddMinutes(30) || minimumCutoff > preparedAt.AddHours(24) ||
                !await ActiveClaim(claim, preparedAt).AnyAsync(token))
            {
                return null;
            }

            PaymentAttempt? attempt = await LoadClaimedAttemptAsync(claim, token);
            bool unknownReplay = attempt is not null &&
                (PaymentAttemptStatusEnum)attempt.PaymentAttemptStatusId == PaymentAttemptStatusEnum.Unknown &&
                claim.ReplayKind == CheckoutDispatchReplayKind.UnknownRedrive &&
                attempt.ProviderCheckoutSessionId is null;
            if (unknownReplay)
            {
                return attempt;
            }

            RegistrationOrder? order = await dbContext.RegistrationOrders
                .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationFinalizationWorkerCrossTenantQueue)
                .SingleOrDefaultAsync(value => value.TenantId == claim.TenantId && value.Id == claim.RegistrationOrderId, token);
            if (attempt is null || order is null || attempt.ProviderCheckoutSessionId is not null || attempt.ProviderPaymentId is not null ||
                (RegistrationOrderStatusEnum)order.RegistrationOrderStatusId != RegistrationOrderStatusEnum.AwaitingPayment ||
                order.TotalDueMinorSnapshot <= 0 ||
                (PaymentAttemptStatusEnum)attempt.PaymentAttemptStatusId is not (PaymentAttemptStatusEnum.Created or PaymentAttemptStatusEnum.DispatchPending))
            {
                return null;
            }

            bool saleStopped = await dbContext.PaidCheckoutSaleControls
                .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationFinalizationWorkerCrossTenantQueue)
                .AnyAsync(value =>
                    value.TenantId == claim.TenantId &&
                    value.IsStopped &&
                    (value.EventId == null || value.EventId == order.EventId),
                    token);
            if (saleStopped)
            {
                return null;
            }

            List<RegistrationInventoryHold> activeHolds = await dbContext.RegistrationInventoryHolds
                .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationFinalizationWorkerCrossTenantQueue)
                .Where(value => value.TenantId == claim.TenantId && value.RegistrationOrderId == claim.RegistrationOrderId &&
                                value.RegistrationInventoryHoldStatusId == (int)RegistrationInventoryHoldStatusEnum.Active)
                .ToListAsync(token);
            DateTime sharedCutoff = new[] { minimumCutoff, attempt.ExpiresAt ?? minimumCutoff, order.ExpiresAt ?? minimumCutoff }
                .Concat(activeHolds.Select(value => value.ExpiresAt))
                .Max();
            if (sharedCutoff > preparedAt.AddHours(24))
            {
                return null;
            }

            _ = attempt.ExtendPaymentCutoff(sharedCutoff, preparedAt);
            _ = order.ExtendPaymentCutoff(sharedCutoff, preparedAt);
            foreach (RegistrationInventoryHold hold in activeHolds)
            {
                _ = hold.ExtendPaymentCutoff(sharedCutoff, preparedAt);
            }

            _ = attempt.MarkDispatchPending(preparedAt, providerRequestId: null);
            await dbContext.SaveChangesAsync(token);
            return attempt;
        }, cancellationToken, System.Data.IsolationLevel.Serializable);

    public Task<CheckoutDispatchConfigurationDisposition> DeferCheckoutDispatchForConfigurationAsync(
        CheckoutDispatchClaim claim,
        string failureCode,
        DateTime nextAttemptAt,
        DateTime observedAt,
        CancellationToken cancellationToken) =>
        ExecuteFencedTransactionAsync<CheckoutDispatchConfigurationDisposition>(async token =>
        {
            string normalized = failureCode?.Trim() ?? string.Empty;
            if (normalized.Length is 0 or > CheckoutDispatchEffect.MaxFailureCodeLength || normalized.Any(char.IsControl) ||
                observedAt.Kind != DateTimeKind.Utc || nextAttemptAt.Kind != DateTimeKind.Utc || nextAttemptAt <= observedAt ||
                !await ActiveClaim(claim, observedAt).AnyAsync(token))
            {
                return CheckoutDispatchConfigurationDisposition.Stale;
            }

            PaymentAttempt? attempt = await LoadClaimedAttemptAsync(claim, token);
            if (attempt is null)
            {
                return CheckoutDispatchConfigurationDisposition.Stale;
            }

            bool expiredBeforeHandoff = attempt.ProviderCheckoutSessionId is null && attempt.ProviderPaymentId is null &&
                attempt.ExpiresAt is { } cutoff && cutoff <= observedAt;
            if (expiredBeforeHandoff)
            {
                return CheckoutDispatchConfigurationDisposition.RequiresLifecycleCancellation;
            }

            int deferred = await ActiveClaim(claim, observedAt).ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.Status, OutboxMessageStatus.Failed)
                .SetProperty(value => value.NextAttemptAt, nextAttemptAt)
                .SetProperty(value => value.LastFailureCode, normalized)
                .SetProperty(value => value.ProcessingLeaseOwner, (string?)null)
                .SetProperty(value => value.ProcessingLeaseToken, (Guid?)null)
                .SetProperty(value => value.ProcessingLeaseExpiresAt, (DateTime?)null)
                .SetProperty(value => value.UpdatedAt, observedAt), token);
            return deferred == 1 ? CheckoutDispatchConfigurationDisposition.Deferred : CheckoutDispatchConfigurationDisposition.Stale;
        }, cancellationToken);

    public async Task<bool> CancelExpiredConfigurationBlockedAsync(
        CheckoutDispatchClaim claim,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        if (observedAt.Kind != DateTimeKind.Utc)
        {
            return false;
        }

        CheckoutDispatchEffect? effect = await dbContext.CheckoutDispatchEffects
            .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationFinalizationWorkerCrossTenantQueue)
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.TenantId == claim.TenantId && value.Id == claim.EffectId &&
                                           value.RegistrationOrderId == claim.RegistrationOrderId &&
                                           value.PaymentAttemptId == claim.PaymentAttemptId &&
                                           value.ProcessingFence == claim.ProcessingFence &&
                                           value.AttemptCount == claim.AttemptCount,
                cancellationToken);
        PaymentAttempt? attempt = await LoadClaimedAttemptAsync(claim, cancellationToken);
        if (effect is null || attempt is null || attempt.ProviderCheckoutSessionId is not null || attempt.ProviderPaymentId is not null)
        {
            return false;
        }

        if (effect.Status == OutboxMessageStatus.DeadLettered &&
            effect.LastFailureCode == "checkout_configuration_expired_before_handoff" &&
            attempt.PaymentAttemptStatusId == (int)PaymentAttemptStatusEnum.Cancelled)
        {
            return true;
        }

        if (attempt.ExpiresAt is not { } cutoff || cutoff > observedAt ||
            !await ActiveClaim(claim, observedAt).AnyAsync(cancellationToken))
        {
            return false;
        }

        DateTime terminalAt = observedAt > attempt.LastStatusObservedAt ? observedAt : attempt.LastStatusObservedAt.AddTicks(1);
        _ = attempt.MarkCancelled(terminalAt, null);
        _ = attempt.TryReleaseActiveSlot(terminalAt);
        int cancelled = await ActiveClaim(claim, observedAt).ExecuteUpdateAsync(setters => setters
            .SetProperty(value => value.Status, OutboxMessageStatus.DeadLettered)
            .SetProperty(value => value.ParkedAt, terminalAt)
            .SetProperty(value => value.LastFailureCode, "checkout_configuration_expired_before_handoff")
            .SetProperty(value => value.ProcessingLeaseOwner, (string?)null)
            .SetProperty(value => value.ProcessingLeaseToken, (Guid?)null)
            .SetProperty(value => value.ProcessingLeaseExpiresAt, (DateTime?)null)
            .SetProperty(value => value.UpdatedAt, terminalAt), cancellationToken);
        return cancelled == 1;
    }

}
