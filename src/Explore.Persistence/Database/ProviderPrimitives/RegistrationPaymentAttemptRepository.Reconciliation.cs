using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
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
        string leaseOwner,
        int batchSize,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(leaseOwner) || batchSize is < 1 or > 50 || leaseDuration <= TimeSpan.Zero)
        {
            return [];
        }

        if (string.Equals(dbContext.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
        {
            return await ClaimDueReconciliationsPostgreSqlAsync(
                leaseOwner.Trim(), batchSize, claimedAt, leaseDuration, cancellationToken);
        }

        IQueryable<PaymentReconciliationEffect> claimable =
            dbContext.PaymentReconciliationEffects
            .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationFinalizationWorkerCrossTenantQueue)
            .Where(value =>
                ((value.Status == OutboxMessageStatus.Pending || value.Status == OutboxMessageStatus.Failed) && value.NextAttemptAt <= claimedAt) ||
                (value.Status == OutboxMessageStatus.Processing && value.ProcessingLeaseExpiresAt <= claimedAt));
        const int maximumContentionPasses = 50;
        DateTime leaseExpiresAt = claimedAt.Add(leaseDuration);
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

            Guid leaseToken = Guid.CreateVersion7();
            int claimed = await claimable
                .Where(value => candidateIds.Contains(value.Id))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(value => value.Status, OutboxMessageStatus.Processing)
                    .SetProperty(value => value.ProcessingLeaseOwner, leaseOwner.Trim())
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

        return claims;
    }

    private async Task<IReadOnlyList<PaymentReconciliationClaim>> ClaimDueReconciliationsPostgreSqlAsync(
        string leaseOwner,
        int batchSize,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        Guid leaseToken = Guid.CreateVersion7();
        DateTime leaseExpiresAt = claimedAt.Add(leaseDuration);
        List<PostgreSqlReconciliationClaimRow> rows = await dbContext.Database.SqlQuery<PostgreSqlReconciliationClaimRow>($$"""
            WITH locked AS
            (
                SELECT tenant_id, id, COALESCE(next_attempt_at, created_at) AS due_at, created_at
                FROM payment_reconciliation_effects
                WHERE ((status IN (1, 4) AND next_attempt_at <= {{claimedAt}})
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
            SELECT * FROM updated ORDER BY claim_order
            """).ToListAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        return rows.Select(row => new PaymentReconciliationClaim(
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
        PaymentReconciliationEffect? effect = await dbContext.PaymentReconciliationEffects
            .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationFinalizationWorkerCrossTenantQueue)
            .SingleOrDefaultAsync(value => value.TenantId == claim.TenantId && value.Id == claim.EffectId, cancellationToken);
        effect?.EnsureClaim(claim.LeaseToken, claim.ProcessingFence, observedAt);
        return effect is null
            ? null
            : await dbContext.PaymentAttempts
                .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationFinalizationWorkerCrossTenantQueue)
                .AsNoTracking()
                .SingleOrDefaultAsync(value => value.TenantId == claim.TenantId && value.Id == claim.PaymentAttemptId, cancellationToken);
    }

    public Task<bool> SettleReconciliationAsync(
        PaymentReconciliationClaim claim,
        PaymentReconciliationDecision decision,
        CancellationToken cancellationToken) =>
        ExecuteFencedTransactionAsync(async token =>
        {
            PaymentReconciliationEffect? effect = await dbContext.PaymentReconciliationEffects
                .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationFinalizationWorkerCrossTenantQueue)
                .SingleOrDefaultAsync(value => value.TenantId == claim.TenantId && value.Id == claim.EffectId, token);
            if (effect is null)
            {
                return false;
            }

            try
            {
                effect.EnsureClaim(claim.LeaseToken, claim.ProcessingFence, decision.ObservedAt);
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            PaymentAttempt? attempt = await dbContext.PaymentAttempts
                .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationFinalizationWorkerCrossTenantQueue)
                .SingleOrDefaultAsync(value => value.TenantId == claim.TenantId && value.Id == claim.PaymentAttemptId, token);
            if (attempt is null)
            {
                effect.Park("payment_attempt_missing", decision.ObservedAt);
                await dbContext.SaveChangesAsync(token);
                return true;
            }

            bool applied;
            try
            {
                applied = ApplyPaymentDecision(attempt, decision);
            }
            catch (InvalidOperationException)
            {
                effect.Park("payment_reconciliation_terminal_conflict", decision.ObservedAt);
                await dbContext.SaveChangesAsync(token);
                return true;
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
                await dbContext.SaveChangesAsync(token);
                return true;
            }
            if (decision.Status == PaymentAttemptStatusEnum.Succeeded &&
                decision.CheckoutSessionId is not null && decision.PaymentId is not null &&
                !await dbContext.PaymentSucceededObservations
                    .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationFinalizationWorkerCrossTenantQueue)
                    .AnyAsync(value => value.TenantId == claim.TenantId && value.PaymentAttemptId == claim.PaymentAttemptId, token))
            {
                await dbContext.PaymentSucceededObservations.AddAsync(PaymentSucceededObservation.Create(
                    attempt,
                    effect.SourceIncomingWebhookMessageId,
                    decision.CheckoutSessionId,
                    decision.PaymentId,
                    decision.ProviderRequestId,
                    decision.ObservedAt), token);
            }

            if (decision.Status == PaymentAttemptStatusEnum.Succeeded && succeededConsistently)
            {
                RegistrationFinalizationEffect? finalizationEffect = await dbContext.RegistrationFinalizationEffects
                    .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationFinalizationWorkerCrossTenantQueue)
                    .SingleOrDefaultAsync(value => value.TenantId == claim.TenantId &&
                                                   value.RegistrationOrderId == attempt.RegistrationOrderId,
                        token);
                if (finalizationEffect is null)
                {
                    RegistrationOrder? order = await dbContext.RegistrationOrders
                        .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationFinalizationWorkerCrossTenantQueue)
                        .SingleOrDefaultAsync(value => value.TenantId == claim.TenantId &&
                                                       value.Id == attempt.RegistrationOrderId,
                            token);
                    if (order is not null)
                    {
                        await dbContext.RegistrationFinalizationEffects.AddAsync(
                            RegistrationFinalizationEffect.Create(order, decision.ObservedAt), token);
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

            await dbContext.SaveChangesAsync(token);
            return true;
        }, cancellationToken);

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
