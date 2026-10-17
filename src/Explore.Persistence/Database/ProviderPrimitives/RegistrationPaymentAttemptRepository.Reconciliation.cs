using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class RegistrationPaymentAttemptRepository
{
    public Task<PaymentAttempt?> FindByProviderObjectAsync(
        Guid tenantId,
        string externalAccountId,
        string providerCheckoutSessionId,
        CancellationToken cancellationToken) =>
        dbContext.PaymentAttempts
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .SingleOrDefaultAsync(value =>
                value.TenantId == tenantId &&
                value.RecipientSnapshot.ExternalAccountId == externalAccountId &&
                value.ProviderCheckoutSessionId == providerCheckoutSessionId,
                cancellationToken);

    public async Task EnsureReconciliationDueAsync(
        PaymentAttempt attempt,
        Guid? sourceIncomingWebhookMessageId,
        DateTime dueAt,
        CancellationToken cancellationToken,
        Guid? checkoutDispatchEffectId = null,
        DateTime? checkoutDispatchUnknownAt = null,
        long? checkoutDispatchProcessingFence = null,
        int? checkoutDispatchAttemptCount = null)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        PaymentReconciliationEffect? existing = await dbContext.PaymentReconciliationEffects
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .SingleOrDefaultAsync(value => value.TenantId == attempt.TenantId && value.PaymentAttemptId == attempt.Id, cancellationToken);
        if (existing is null)
        {
            await dbContext.PaymentReconciliationEffects.AddAsync(
                PaymentReconciliationEffect.Create(
                    attempt,
                    dueAt,
                    sourceIncomingWebhookMessageId,
                    checkoutDispatchEffectId,
                    checkoutDispatchUnknownAt,
                    checkoutDispatchProcessingFence,
                    checkoutDispatchAttemptCount),
                cancellationToken);
        }
        else
        {
            existing.MakeDue(
                dueAt,
                sourceIncomingWebhookMessageId,
                checkoutDispatchEffectId,
                checkoutDispatchUnknownAt,
                checkoutDispatchProcessingFence,
                checkoutDispatchAttemptCount);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PaymentReconciliationClaim>> ClaimDueReconciliationsAsync(
        PaymentReconciliationAcquisition acquisition, DateTime observedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(acquisition);
        if (!acquisition.IsValidAt(observedAt) || acquisition.LeaseExpiresAt <= observedAt)
            return [];
        bool attempted = false;
        long startedAt = TimeProvider.System.GetTimestamp();
        return await dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            bool recoverOnly = attempted;
            attempted = true;
            return await AcquireReconciliationsAsync(acquisition,
                observedAt.Add(TimeProvider.System.GetElapsedTime(startedAt)), recoverOnly, cancellationToken);
        });
    }

    public Task<IReadOnlyList<PaymentReconciliationClaim>> RecoverReconciliationAcquisitionAsync(
        PaymentReconciliationAcquisition acquisition, DateTime observedAt, CancellationToken cancellationToken) =>
        AcquireReconciliationsAsync(acquisition, observedAt, true, cancellationToken);

    private async Task<IReadOnlyList<PaymentReconciliationClaim>> AcquireReconciliationsAsync(
        PaymentReconciliationAcquisition acquisition, DateTime observedAt, bool recoverOnly,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(acquisition);
        if (!acquisition.IsValidAt(observedAt) || acquisition.LeaseExpiresAt <= observedAt)
            return [];
        if (string.Equals(dbContext.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
        {
            return await ClaimDueReconciliationsPostgreSqlAsync(
                acquisition, observedAt, recoverOnly, cancellationToken);
        }

        Guid leaseToken = acquisition.LeaseToken;
        string leaseOwner = acquisition.LeaseOwner;
        DateTime claimedAt = acquisition.ClaimedAt;
        DateTime leaseExpiresAt = acquisition.LeaseExpiresAt;
        int batchSize = acquisition.BatchSize;
        List<PaymentReconciliationEffect> owned = await dbContext.PaymentReconciliationEffects
            .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationFinalizationWorkerCrossTenantQueue)
            .AsNoTracking()
            .Where(value => value.ProcessingLeaseToken == leaseToken)
            .OrderBy(value => value.Id)
            .Take(51)
            .ToListAsync(cancellationToken);
        if (owned.Count > 0)
        {
            if (owned.Count > batchSize || owned.Any(value =>
                    value.TenantId == Guid.Empty || value.Status != OutboxMessageStatus.Processing ||
                    value.ProcessingLeaseOwner != leaseOwner || value.UpdatedAt != claimedAt ||
                    value.ProcessingLeaseExpiresAt != leaseExpiresAt ||
                    value.ProcessingFence <= 0 || value.AttemptCount <= 0))
                return [];
            return owned.Select(value => new PaymentReconciliationClaim(
                value.TenantId, value.Id, value.PaymentAttemptId, leaseToken, value.ProcessingFence,
                value.AttemptCount, value.CheckoutDispatchEffectId, value.CheckoutDispatchUnknownAt,
                value.CheckoutDispatchProcessingFence, value.CheckoutDispatchAttemptCount)).ToArray();
        }
        if (recoverOnly)
            return [];

        IQueryable<PaymentReconciliationEffect> claimable =
            dbContext.PaymentReconciliationEffects
            .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationFinalizationWorkerCrossTenantQueue)
            .Where(value =>
                ((value.Status == OutboxMessageStatus.Pending || value.Status == OutboxMessageStatus.Failed) && value.NextAttemptAt <= claimedAt) ||
                (value.Status == OutboxMessageStatus.Processing && value.ProcessingLeaseExpiresAt <= claimedAt));
        const int maximumContentionPasses = 50;
        var claims = new List<PaymentReconciliationClaim>(batchSize);
        for (var pass = 0;
             pass < maximumContentionPasses && claims.Count < batchSize;
             pass++)
        {
            List<Guid> candidateIds = await claimable
                .OrderBy(value => value.NextAttemptAt ?? value.CreatedAt)
                .ThenBy(value => value.CreatedAt)
                .ThenBy(value => value.Id)
                .Take(batchSize - claims.Count)
                .Select(value => value.Id)
                .ToListAsync(cancellationToken);
            if (candidateIds.Count == 0)
            {
                break;
            }

            int claimed = await claimable
                .Where(value => candidateIds.Contains(value.Id))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(value => value.Status, OutboxMessageStatus.Processing)
                    .SetProperty(value => value.ProcessingLeaseOwner, leaseOwner)
                    .SetProperty(value => value.ProcessingLeaseToken, leaseToken)
                    .SetProperty(value => value.ProcessingLeaseExpiresAt, leaseExpiresAt)
                    .SetProperty(value => value.ProcessingFence, value => value.ProcessingFence + 1)
                    .SetProperty(value => value.AttemptCount, value => value.AttemptCount + 1)
                    .SetProperty(value => value.NextAttemptAt, (DateTime?)null)
                    .SetProperty(
                        value => value.LastFailureCode,
                        value => value.Status == OutboxMessageStatus.Processing
                            ? "payment_reconciliation_interrupted"
                            : value.LastFailureCode)
                    .SetProperty(
                        value => value.UnknownAt,
                        value => value.Status == OutboxMessageStatus.Processing
                            ? claimedAt
                            : value.UnknownAt)
                    .SetProperty(value => value.UpdatedAt, claimedAt),
                    cancellationToken);
            dbContext.ChangeTracker.Clear();
            if (claimed == 0)
            {
                continue;
            }

            List<PaymentReconciliationEffect> claimedEffects =
                await dbContext.PaymentReconciliationEffects
                    .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationFinalizationWorkerCrossTenantQueue)
                    .AsNoTracking()
                    .Where(value =>
                        candidateIds.Contains(value.Id) &&
                        value.ProcessingLeaseToken == leaseToken &&
                        value.ProcessingLeaseOwner == leaseOwner &&
                        value.UpdatedAt == claimedAt &&
                        value.ProcessingLeaseExpiresAt == leaseExpiresAt &&
                        value.Status == OutboxMessageStatus.Processing)
                    .ToListAsync(cancellationToken);
            IReadOnlyDictionary<Guid, PaymentReconciliationEffect> claimedById =
                claimedEffects.ToDictionary(effect => effect.Id);
            claims.AddRange(candidateIds
                .Where(claimedById.ContainsKey)
                .Select(effectId =>
                {
                    PaymentReconciliationEffect effect = claimedById[effectId];
                    return new PaymentReconciliationClaim(
                        effect.TenantId,
                        effect.Id,
                        effect.PaymentAttemptId,
                        leaseToken,
                        effect.ProcessingFence,
                        effect.AttemptCount,
                        effect.CheckoutDispatchEffectId,
                        effect.CheckoutDispatchUnknownAt,
                        effect.CheckoutDispatchProcessingFence,
                        effect.CheckoutDispatchAttemptCount);
                }));
        }

        return claims.OrderBy(value => value.EffectId).ToArray();
    }

    private async Task<IReadOnlyList<PaymentReconciliationClaim>> ClaimDueReconciliationsPostgreSqlAsync(
        PaymentReconciliationAcquisition acquisition, DateTime observedAt, bool recoverOnly,
        CancellationToken cancellationToken)
    {
        Guid leaseToken = acquisition.LeaseToken;
        string leaseOwner = acquisition.LeaseOwner;
        int batchSize = acquisition.BatchSize;
        DateTime claimedAt = acquisition.ClaimedAt;
        DateTime leaseExpiresAt = acquisition.LeaseExpiresAt;
        List<PostgreSqlReconciliationClaimRow> rows = await dbContext.Database.SqlQuery<PostgreSqlReconciliationClaimRow>($$"""
            WITH owned AS MATERIALIZED
            (
                SELECT tenant_id, id AS effect_id, payment_attempt_id,
                       processing_lease_token AS lease_token, processing_fence, attempt_count,
                       checkout_dispatch_effect_id, checkout_dispatch_unknown_at,
                       checkout_dispatch_processing_fence, checkout_dispatch_attempt_count,
                       status, processing_lease_owner, processing_lease_expires_at, updated_at
                FROM payment_reconciliation_effects
                WHERE processing_lease_token = {{leaseToken}}
                LIMIT 51
            ), recovered AS
            (
                SELECT tenant_id, effect_id, payment_attempt_id, lease_token, processing_fence,
                       attempt_count, checkout_dispatch_effect_id, checkout_dispatch_unknown_at,
                       checkout_dispatch_processing_fence, checkout_dispatch_attempt_count,
                       row_number() OVER (ORDER BY effect_id) AS claim_order
                FROM owned
                WHERE NOT EXISTS (
                    SELECT 1 FROM owned WHERE status <> 2 OR processing_lease_owner IS DISTINCT FROM {{leaseOwner}}
                        OR updated_at IS DISTINCT FROM {{claimedAt}}
                        OR processing_lease_expires_at IS DISTINCT FROM {{leaseExpiresAt}}
                        OR tenant_id = '00000000-0000-0000-0000-000000000000'::uuid
                        OR processing_fence <= 0 OR attempt_count <= 0)
                    AND (SELECT count(*) FROM owned) <= {{batchSize}}
                    AND {{leaseExpiresAt}} > {{observedAt}}
            ), locked AS
            (
                SELECT tenant_id, id, COALESCE(next_attempt_at, created_at) AS due_at, created_at
                FROM payment_reconciliation_effects
                WHERE NOT {{recoverOnly}} AND NOT EXISTS (SELECT 1 FROM owned)
                    AND {{leaseExpiresAt}} > {{observedAt}}
                    AND ((status IN (1, 4) AND next_attempt_at <= {{claimedAt}})
                    OR (status = 2 AND processing_lease_expires_at <= {{claimedAt}}))
                ORDER BY COALESCE(next_attempt_at, created_at), created_at, id
                LIMIT {{batchSize}}
                FOR UPDATE SKIP LOCKED
            ), candidates AS
            (
                SELECT tenant_id, id,
                       ROW_NUMBER() OVER (ORDER BY due_at, created_at, id) AS claim_order
                FROM locked
            ), updated AS
            (
                UPDATE payment_reconciliation_effects AS effect
                SET status = 2,
                    attempt_count = effect.attempt_count + 1,
                    processing_fence = effect.processing_fence + 1,
                    processing_lease_owner = {{leaseOwner}},
                    processing_lease_token = {{leaseToken}},
                    processing_lease_expires_at = {{leaseExpiresAt}},
                    next_attempt_at = NULL,
                    last_failure_code = CASE WHEN effect.status = 2 THEN 'payment_reconciliation_interrupted' ELSE effect.last_failure_code END,
                    unknown_at = CASE WHEN effect.status = 2 THEN {{claimedAt}} ELSE effect.unknown_at END,
                    updated_at = {{claimedAt}}
                FROM candidates
                WHERE effect.tenant_id = candidates.tenant_id AND effect.id = candidates.id
                RETURNING effect.tenant_id, effect.id AS effect_id,
                          effect.payment_attempt_id,
                          effect.processing_lease_token AS lease_token,
                          effect.processing_fence,
                          effect.attempt_count,
                          effect.checkout_dispatch_effect_id,
                          effect.checkout_dispatch_unknown_at,
                          effect.checkout_dispatch_processing_fence,
                          effect.checkout_dispatch_attempt_count,
                          candidates.claim_order
            )
            SELECT * FROM recovered
            UNION ALL
            SELECT * FROM updated
            """).ToListAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        return rows.OrderBy(row => row.EffectId).Select(row => new PaymentReconciliationClaim(
            row.TenantId,
            row.EffectId,
            row.PaymentAttemptId,
            row.LeaseToken,
            row.ProcessingFence,
            row.AttemptCount,
            row.CheckoutDispatchEffectId,
            row.CheckoutDispatchUnknownAt,
            row.CheckoutDispatchProcessingFence,
            row.CheckoutDispatchAttemptCount)).ToArray();
    }

    public async Task<PaymentAttempt?> GetReconciliationAttemptAsync(
        PaymentReconciliationClaim claim,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        bool active = await ActiveReconciliationClaim(claim, observedAt)
            .AsNoTracking().AnyAsync(cancellationToken);
        return !active
            ? null
            : await dbContext.PaymentAttempts
                .AsNoTracking()
                .SingleOrDefaultAsync(value => value.TenantId == claim.TenantId && value.Id == claim.PaymentAttemptId, cancellationToken);
    }

    private IQueryable<PaymentReconciliationEffect> ActiveReconciliationClaim(
        PaymentReconciliationClaim claim, DateTime observedAt) =>
        dbContext.PaymentReconciliationEffects.Where(value =>
            value.TenantId == claim.TenantId && value.Id == claim.EffectId &&
            value.PaymentAttemptId == claim.PaymentAttemptId &&
            value.Status == OutboxMessageStatus.Processing &&
            value.ProcessingLeaseToken == claim.LeaseToken && value.ProcessingFence == claim.ProcessingFence &&
            value.AttemptCount == claim.AttemptCount && value.ProcessingLeaseExpiresAt > observedAt &&
            value.CheckoutDispatchEffectId == claim.CheckoutDispatchEffectId &&
            value.CheckoutDispatchUnknownAt == claim.CheckoutDispatchUnknownAt &&
            value.CheckoutDispatchProcessingFence == claim.CheckoutDispatchProcessingFence &&
            value.CheckoutDispatchAttemptCount == claim.CheckoutDispatchAttemptCount);

    public Task<bool> SettleReconciliationAsync(
        PaymentReconciliationClaim claim,
        PaymentReconciliationDecision decision,
        CancellationToken cancellationToken) =>
        ExecuteFencedTransactionAsync(async token =>
            (await SettleReconciliationCoreAsync(claim, decision, false, token)).Settled,
            cancellationToken);

    public Task<PaymentReconciliationRecoveryResult> RecoverUnknownDispatchReconciliationAsync(
        PaymentReconciliationClaim claim,
        PaymentReconciliationDecision decision,
        CancellationToken cancellationToken) =>
        ExecuteFencedTransactionAsync(
            token => SettleReconciliationCoreAsync(claim, decision, true, token), cancellationToken);

    private async Task<PaymentReconciliationRecoveryResult> SettleReconciliationCoreAsync(
        PaymentReconciliationClaim claim,
        PaymentReconciliationDecision decision,
        bool recoverUnknownDispatch,
        CancellationToken token)
    {
        // The row fence precedes the read so a reclaim cannot replace the live
        // identity between validation and the payment/outbox commit.
        await RelationalEntityRowFence.AcquireAsync<PaymentReconciliationEffect>(
            dbContext, claim.TenantId, value => value.Id, claim.EffectId, token);
        PaymentReconciliationEffect? effect = await ActiveReconciliationClaim(claim, decision.ObservedAt)
            .SingleOrDefaultAsync(token);
        if (effect is null)
        {
            return new(false, false);
        }

        PaymentAttempt? attempt = await dbContext.PaymentAttempts
            .SingleOrDefaultAsync(value => value.TenantId == effect.TenantId &&
                value.Id == effect.PaymentAttemptId && value.RegistrationOrderId == effect.RegistrationOrderId, token);
        if (attempt is null)
        {
            effect.Park("payment_attempt_missing", decision.ObservedAt);
            await dbContext.SaveChangesAsync(token);
            return new(true, false);
        }

        PaymentSucceededObservation? observation = null;
        RegistrationFinalizationEffect? finalizationEffect = null;
        bool requeuedDispatch = false;
        if (recoverUnknownDispatch &&
            string.IsNullOrWhiteSpace(attempt.ProviderCheckoutSessionId) &&
            decision.Disposition == PaymentReconciliationDisposition.Retry &&
            decision.Status == PaymentAttemptStatusEnum.Unknown &&
            claim.CheckoutDispatchEffectId is { } dispatchId &&
            claim.CheckoutDispatchUnknownAt is { } unknownAt &&
            claim.CheckoutDispatchProcessingFence is { } dispatchFence &&
            claim.CheckoutDispatchAttemptCount is { } dispatchAttemptCount)
        {
            // Replay only the captured dispatch under the same live reconciliation
            // fence. The persisted payment idempotency key is never regenerated.
            int rows = await dbContext.CheckoutDispatchEffects
                .Where(value => value.TenantId == claim.TenantId && value.Id == dispatchId &&
                    value.PaymentAttemptId == attempt.Id && value.RegistrationOrderId == attempt.RegistrationOrderId &&
                    value.Status == OutboxMessageStatus.Unknown && value.UnknownAt == unknownAt &&
                    value.ProcessingFence == dispatchFence && value.AttemptCount == dispatchAttemptCount)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(value => value.Status, OutboxMessageStatus.Failed)
                    .SetProperty(value => value.NextAttemptAt, decision.ObservedAt)
                    .SetProperty(value => value.ProcessingLeaseOwner, (string?)null)
                    .SetProperty(value => value.ProcessingLeaseToken, (Guid?)null)
                    .SetProperty(value => value.ProcessingLeaseExpiresAt, (DateTime?)null)
                    .SetProperty(value => value.UpdatedAt, decision.ObservedAt), token);
            requeuedDispatch = rows == 1;
        }

        bool applied;
        try
        {
            applied = ApplyPaymentDecision(attempt, decision);
        }
        catch (InvalidOperationException)
        {
            effect.Park("payment_reconciliation_terminal_conflict", decision.ObservedAt);
            dbContext.RetainNativePaymentReconciliationOwner(claim, effect, attempt, observation, finalizationEffect);
            await dbContext.SaveChangesAsync(token);
            return new(true, requeuedDispatch);
        }
        await ClosePendingMaterialChangeChoicesAsync(attempt, decision.ObservedAt, token);
        if (decision.Disposition == PaymentReconciliationDisposition.Complete &&
            decision.Status is PaymentAttemptStatusEnum.Failed or PaymentAttemptStatusEnum.Cancelled &&
            attempt.PaymentAttemptStatusId == (int)decision.Status)
        {
            _ = attempt.TryReleaseActiveSlot(decision.ObservedAt);
        }
        bool succeededConsistently =
            attempt.PaymentAttemptStatusId == (int)PaymentAttemptStatusEnum.Succeeded &&
            string.Equals(attempt.ProviderCheckoutSessionId, decision.CheckoutSessionId, StringComparison.Ordinal) &&
            string.Equals(attempt.ProviderPaymentId, decision.PaymentId, StringComparison.Ordinal);
        if (decision.Status == PaymentAttemptStatusEnum.Succeeded && !applied && !succeededConsistently)
        {
            DateTime settledAt = decision.ObservedAt > attempt.LastStatusObservedAt
                ? decision.ObservedAt
                : attempt.LastStatusObservedAt.AddTicks(1);
            effect.Retry(
                "payment_reconciliation_stale_success",
                settledAt.AddMinutes(2),
                settledAt,
                unknown: true);
            dbContext.RetainNativePaymentReconciliationOwner(claim, effect, attempt, observation, finalizationEffect);
            await dbContext.SaveChangesAsync(token);
            return new(true, requeuedDispatch);
        }
        if (decision.Status == PaymentAttemptStatusEnum.Succeeded &&
            decision.CheckoutSessionId is not null && decision.PaymentId is not null &&
            !await dbContext.PaymentSucceededObservations
                .AnyAsync(value => value.TenantId == claim.TenantId && value.PaymentAttemptId == claim.PaymentAttemptId, token))
        {
            observation = PaymentSucceededObservation.Create(
                attempt,
                effect.SourceIncomingWebhookMessageId,
                decision.CheckoutSessionId,
                decision.PaymentId,
                decision.ProviderRequestId,
                decision.ObservedAt);
            await dbContext.PaymentSucceededObservations.AddAsync(observation, token);
        }

        if (decision.Status == PaymentAttemptStatusEnum.Succeeded && succeededConsistently)
        {
            finalizationEffect = await dbContext.RegistrationFinalizationEffects
                .SingleOrDefaultAsync(value => value.TenantId == claim.TenantId &&
                                               value.RegistrationOrderId == attempt.RegistrationOrderId,
                    token);
            if (finalizationEffect is null)
            {
                RegistrationOrder? order = await dbContext.RegistrationOrders
                    .SingleOrDefaultAsync(value => value.TenantId == claim.TenantId &&
                                                   value.Id == attempt.RegistrationOrderId,
                        token);
                if (order is not null)
                {
                    finalizationEffect = RegistrationFinalizationEffect.Create(order, decision.ObservedAt);
                    await dbContext.RegistrationFinalizationEffects.AddAsync(finalizationEffect, token);
                }
            }
            else
            {
                finalizationEffect.Request(decision.ObservedAt);
            }
        }

        switch (decision.Disposition)
        {
            case PaymentReconciliationDisposition.Complete:
                effect.Complete(decision.ObservedAt);
                break;
            case PaymentReconciliationDisposition.Park:
                effect.Park(decision.FailureCode, decision.ObservedAt);
                break;
            default:
                effect.Retry(decision.FailureCode, decision.NextAttemptAt ?? decision.ObservedAt.AddMinutes(5), decision.ObservedAt, decision.Status == PaymentAttemptStatusEnum.Unknown);
                break;
        }

        dbContext.RetainNativePaymentReconciliationOwner(claim, effect, attempt, observation, finalizationEffect);
        await dbContext.SaveChangesAsync(token);
        return new(true, requeuedDispatch);
    }

    public async Task<PaymentReconciliationHealth> GetReconciliationHealthAsync(
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        IQueryable<PaymentReconciliationEffect> effects = dbContext.PaymentReconciliationEffects
            .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationFinalizationWorkerCrossTenantQueue)
            .AsNoTracking();
        int due = await effects.CountAsync(value =>
            ((value.Status == OutboxMessageStatus.Pending || value.Status == OutboxMessageStatus.Failed) && value.NextAttemptAt <= observedAt) ||
            (value.Status == OutboxMessageStatus.Processing && value.ProcessingLeaseExpiresAt <= observedAt), cancellationToken);
        int unknown = await effects.CountAsync(value => value.UnknownAt != null && value.Status == OutboxMessageStatus.Failed, cancellationToken);
        int parked = await effects.CountAsync(value => value.Status == OutboxMessageStatus.DeadLettered, cancellationToken);
        DateTime? oldest = await effects
            .Where(value => (value.Status == OutboxMessageStatus.Pending || value.Status == OutboxMessageStatus.Failed) && value.NextAttemptAt <= observedAt)
            .MinAsync(value => (DateTime?)value.NextAttemptAt, cancellationToken);
        int configurationBlocked = await dbContext.CheckoutDispatchEffects
            .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationFinalizationWorkerCrossTenantQueue)
            .AsNoTracking()
            .CountAsync(value => value.Status == OutboxMessageStatus.Failed &&
                                 value.LastFailureCode != null &&
                                 value.LastFailureCode.StartsWith("checkout_provider_"), cancellationToken);
        configurationBlocked += await effects.CountAsync(value => value.Status == OutboxMessageStatus.Failed &&
            value.LastFailureCode != null && value.LastFailureCode.StartsWith("checkout_provider_"), cancellationToken);
        int duplicateSucceededOrders = await dbContext.PaymentSucceededObservations
            .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationFinalizationWorkerCrossTenantQueue)
            .AsNoTracking()
            .GroupBy(value => new { value.TenantId, value.RegistrationOrderId })
            .CountAsync(group => group.Count() > 1, cancellationToken);
        return new(due, unknown, parked, oldest, configurationBlocked, duplicateSucceededOrders);
    }

    private sealed class PostgreSqlReconciliationClaimRow
    {
        public Guid TenantId { get; init; }
        public Guid EffectId { get; init; }
        public Guid PaymentAttemptId { get; init; }
        public Guid LeaseToken { get; init; }
        public long ProcessingFence { get; init; }
        public int AttemptCount { get; init; }
        public Guid? CheckoutDispatchEffectId { get; init; }
        public DateTime? CheckoutDispatchUnknownAt { get; init; }
        public long? CheckoutDispatchProcessingFence { get; init; }
        public int? CheckoutDispatchAttemptCount { get; init; }
        public long ClaimOrder { get; init; }
    }

    private static bool ApplyPaymentDecision(PaymentAttempt attempt, PaymentReconciliationDecision decision)
    {
        switch (decision.Status)
        {
            case PaymentAttemptStatusEnum.RequiresAction when decision.CheckoutSessionId is not null:
                return attempt.MarkRequiresAction(decision.CheckoutSessionId, decision.ObservedAt, decision.ProviderRequestId);
            case PaymentAttemptStatusEnum.Processing when decision.PaymentId is not null:
                return attempt.MarkProcessing(decision.CheckoutSessionId, decision.PaymentId, decision.ObservedAt, decision.ProviderRequestId);
            case PaymentAttemptStatusEnum.Succeeded when decision.CheckoutSessionId is not null && decision.PaymentId is not null:
                return attempt.MarkSucceededFromCheckout(decision.CheckoutSessionId, decision.PaymentId, decision.ObservedAt, decision.ProviderRequestId);
            case PaymentAttemptStatusEnum.Failed when decision.CheckoutSessionId is not null && decision.PaymentId is not null:
                return attempt.MarkFailedFromCheckout(decision.CheckoutSessionId, decision.PaymentId, decision.ObservedAt, decision.ProviderRequestId);
            case PaymentAttemptStatusEnum.Cancelled when decision.CheckoutSessionId is not null:
                return attempt.MarkCancelledFromCheckout(decision.CheckoutSessionId, decision.ObservedAt, decision.ProviderRequestId);
            case PaymentAttemptStatusEnum.Unknown:
                return attempt.MarkUnknown(decision.ObservedAt, decision.ProviderRequestId);
            default:
                return false;
        }
    }
}
