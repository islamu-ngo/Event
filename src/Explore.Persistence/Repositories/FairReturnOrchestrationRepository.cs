using System.Linq.Expressions;
using Explore.Application.Contracts.Waitlist;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Interfaces;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class FairReturnOrchestrationRepository(
    ExploreDbContext dbContext) :
    IFairReturnOrchestrationRepository
{
    public const string CanonicalFenceOrder =
        "effect>payment-intent>binding>" +
        "replacement-payment>reserved-refund>" +
        "refund-intent>outbox";

    public Task<WaitlistPaymentIntent>
        CreatePaymentIntentAsync(
            WaitlistPaymentIntent intent,
            RefundAttempt reservedRefundAttempt,
            FairReturnOrchestrationEffect effect,
            CancellationToken cancellationToken) =>
        ExecuteFencedAsync(async () =>
        {
            await FenceAsync<FairReturnSourceBinding>(
                intent.TenantId,
                intent.FairReturnSourceBindingId,
                binding => binding.Id,
                cancellationToken);
            WaitlistPaymentIntent? existing =
                await dbContext.WaitlistPaymentIntents
                    .SingleOrDefaultAsync(value =>
                        value.TenantId ==
                            intent.TenantId
                        && value.StableOperationId ==
                            intent.StableOperationId,
                        cancellationToken);
            if (existing is not null)
            {
                return existing;
            }
            dbContext.RefundAttempts.Add(
                reservedRefundAttempt);
            dbContext.WaitlistPaymentIntents.Add(
                intent);
            dbContext.FairReturnOrchestrationEffects
                .Add(effect);
            await dbContext.SaveChangesAsync(
                cancellationToken);
            return intent;
        }, cancellationToken);

    public Task<IReadOnlyList<
        FairReturnOrchestrationClaim>> TryClaimDueAsync(
            DateTime claimedAtUtc,
            string leaseOwner,
            Guid? effectId,
            int batchSize,
            int MaximumEffectsPerTenant,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
    {
        FairReturnSupplyPolicy.RequireUtc(
            claimedAtUtc,
            nameof(claimedAtUtc));
        if (batchSize is < 1 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(batchSize));
        }
        if (MaximumEffectsPerTenant is < 1
            || MaximumEffectsPerTenant > batchSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumEffectsPerTenant));
        }
        return ExecuteFencedAsync<
            IReadOnlyList<
                FairReturnOrchestrationClaim>>(
            async () =>
            {
                IQueryable<
                    FairReturnOrchestrationEffect>
                    dueQuery = dbContext
                    .FairReturnOrchestrationEffects
                    .AsNoTracking()
                    .Where(value =>
                        (!effectId.HasValue
                         || value.Id ==
                            effectId.Value)
                        && (value.StatusId ==
                            (int)
                            FairReturnOrchestrationEffectStatus
                                .Pending
                        && value.NextAttemptAt <=
                            claimedAtUtc
                        || value.StatusId ==
                            (int)
                            FairReturnOrchestrationEffectStatus
                                .Processing
                        && value.LeaseExpiresAt <=
                            claimedAtUtc));
                Guid[] tenantOrder =
                    await dueQuery
                        .GroupBy(value =>
                            value.TenantId)
                        .Select(group => new
                        {
                            TenantId = group.Key,
                            FirstCursor = group.Min(
                                value =>
                                    value.StableCursor),
                        })
                        .OrderBy(value =>
                            value.FirstCursor)
                        .ThenBy(value =>
                            value.TenantId)
                        .Take(batchSize)
                        .Select(value =>
                            value.TenantId)
                        .ToArrayAsync(
                            cancellationToken);
                int perTenantLimit =
                    tenantOrder.Length == 0
                        ? 0
                        : Math.Min(
                            MaximumEffectsPerTenant,
                            (batchSize
                             + tenantOrder.Length
                             - 1)
                            / tenantOrder.Length);
                var boundedDue = new List<
                    FairReturnOrchestrationEffect>(
                        batchSize);
                foreach (Guid tenantId
                         in tenantOrder)
                {
                    FairReturnOrchestrationEffect[]
                        tenantDue = await dueQuery
                        .Where(value =>
                            value.TenantId == tenantId)
                        .OrderBy(value =>
                            value.StableCursor)
                        .ThenBy(value => value.Id)
                        .Take(perTenantLimit)
                        .ToArrayAsync(
                            cancellationToken);
                    boundedDue.AddRange(tenantDue);
                }
                FairReturnOrchestrationEffect[]
                    selected = RoundRobin(
                        boundedDue,
                        batchSize,
                        MaximumEffectsPerTenant);
                var claims =
                    new List<
                        FairReturnOrchestrationClaim>(
                        selected.Length);
                foreach (
                    FairReturnOrchestrationEffect
                        snapshot in selected)
                {
                    await FenceAsync<
                        FairReturnOrchestrationEffect>(
                            snapshot.TenantId,
                            snapshot.Id,
                            effect => effect.Id,
                            cancellationToken);
                    FairReturnOrchestrationEffect?
                        effect =
                        await dbContext
                            .FairReturnOrchestrationEffects
                            .SingleOrDefaultAsync(value =>
                                value.TenantId ==
                                    snapshot.TenantId
                                && value.Id ==
                                    snapshot.Id,
                                cancellationToken);
                    bool expiredLease =
                        effect?.StatusId ==
                            (int)
                            FairReturnOrchestrationEffectStatus
                                .Processing;
                    if (effect is null
                        || !effect.TryClaim(
                            leaseOwner,
                            claimedAtUtc,
                            leaseDuration))
                    {
                        continue;
                    }
                    WaitlistPaymentIntent intent =
                        await dbContext
                            .WaitlistPaymentIntents
                            .SingleAsync(value =>
                                value.TenantId ==
                                    effect.TenantId
                                && value.Id ==
                                    effect
                                        .WaitlistPaymentIntentId,
                                cancellationToken);
                    claims.Add(new
                        FairReturnOrchestrationClaim(
                            effect.TenantId,
                            effect.Id,
                            intent.Id,
                            effect.StableOperationId,
                            intent
                                .ProviderIdempotencyKey,
                            effect.StableCursor,
                            effect.ProcessingFence,
                            effect.AttemptCount,
                            effect.MaximumAttempts,
                            expiredLease));
                }
                await dbContext.SaveChangesAsync(
                    cancellationToken);
                return claims;
            },
            cancellationToken);
    }

    public async Task<PaymentAttempt?>
        GetReplacementPaymentAsync(
            FairReturnOrchestrationClaim claim,
            CancellationToken cancellationToken)
    {
        WaitlistPaymentIntent? intent =
            await dbContext.WaitlistPaymentIntents
                .AsNoTracking()
                .SingleOrDefaultAsync(value =>
                    value.TenantId == claim.TenantId
                    && value.Id ==
                        claim.WaitlistPaymentIntentId,
                    cancellationToken);
        return intent is null
            ? null
            : await dbContext.PaymentAttempts
                .AsNoTracking()
                .SingleOrDefaultAsync(value =>
                    value.TenantId == claim.TenantId
                    && value.Id ==
                        intent.ReplacementPaymentAttemptId,
                    cancellationToken);
    }

    public Task<bool> ObserveReplacementSettlementAsync(
        FairReturnOrchestrationClaim claim,
        DateTime settledAtUtc,
        CancellationToken cancellationToken) =>
        ExecuteFencedAsync(async () =>
        {
            (
                FairReturnOrchestrationEffect? effect,
                WaitlistPaymentIntent? intent) =
                await LockClaimAsync(
                    claim,
                    cancellationToken);
            if (!Owns(effect, claim)
                || intent is null)
            {
                return false;
            }
            intent.ObserveReplacementSettlement(
                settledAtUtc);
            await dbContext.SaveChangesAsync(
                cancellationToken);
            return true;
        }, cancellationToken);

    public Task<WaitlistRefundIntent?>
        CreateRefundIntentAsync(
            FairReturnOrchestrationClaim claim,
            DateTime settledAtUtc,
            CancellationToken cancellationToken) =>
        ExecuteFencedAsync(async () =>
        {
            (
                FairReturnOrchestrationEffect? effect,
                WaitlistPaymentIntent? intent) =
                await LockClaimAsync(
                    claim,
                    cancellationToken);
            if (!Owns(effect, claim)
                || intent is null
                || !intent
                    .ReplacementPaymentSettledAt
                    .HasValue)
            {
                return null;
            }
            WaitlistRefundIntent? existing =
                await dbContext.WaitlistRefundIntents
                    .SingleOrDefaultAsync(value =>
                        value.TenantId == claim.TenantId
                        && value.StableOperationId ==
                            claim.StableOperationId,
                        cancellationToken);
            if (existing is not null)
            {
                return existing;
            }
            RefundAttempt refundAttempt =
                await dbContext.RefundAttempts
                    .SingleAsync(value =>
                        value.TenantId == claim.TenantId
                        && value.Id ==
                            intent.ReservedRefundAttemptId,
                        cancellationToken);
            OutboxMessage trigger =
                RefundOutboxMessageFactory
                    .CreateDispatch(
                        refundAttempt,
                        settledAtUtc);
            WaitlistRefundIntent refundIntent =
                WaitlistRefundIntent.Create(
                    intent.RefundIntentId,
                    intent,
                    trigger.Id,
                    intent
                        .ReplacementPaymentSettledAt
                        .Value);
            dbContext.OutboxMessages.Add(trigger);
            dbContext.WaitlistRefundIntents.Add(
                refundIntent);
            await dbContext.SaveChangesAsync(
                cancellationToken);
            return refundIntent;
        }, cancellationToken);

    public Task<bool> MarkCompletedAsync(
        FairReturnOrchestrationClaim claim,
        DateTime completedAtUtc,
        CancellationToken cancellationToken) =>
        ExecuteFencedAsync(async () =>
        {
            FairReturnOrchestrationEffect? effect =
                await LockEffectAsync(
                    claim,
                    cancellationToken);
            if (effect is null
                || !effect.Complete(
                    claim.ProcessingFence,
                    completedAtUtc))
            {
                return false;
            }
            await dbContext.SaveChangesAsync(
                cancellationToken);
            return true;
        }, cancellationToken);

    public Task<FairReturnDispatchOutcome>
        MarkFailedAsync(
            FairReturnOrchestrationClaim claim,
            string failureCode,
            bool retryable,
            DateTime failedAtUtc,
            DateTime retryAtUtc,
            CancellationToken cancellationToken) =>
        ExecuteFencedAsync(async () =>
        {
            FairReturnOrchestrationEffect? effect =
                await LockEffectAsync(
                    claim,
                    cancellationToken);
            if (!Owns(effect, claim))
            {
                return FairReturnDispatchOutcome
                    .StaleLease;
            }
            FairReturnOrchestrationEffectStatus status =
                effect!.Fail(
                    claim.ProcessingFence,
                    failureCode,
                    retryable,
                    failedAtUtc,
                    retryAtUtc);
            await dbContext.SaveChangesAsync(
                cancellationToken);
            return status ==
                FairReturnOrchestrationEffectStatus
                    .DeadLettered
                ? FairReturnDispatchOutcome
                    .DeadLettered
                : FairReturnDispatchOutcome
                    .RetryScheduled;
        }, cancellationToken);

    public async Task<FairReturnOrchestrationHealth>
        GetHealthAsync(
            DateTime observedAtUtc,
            CancellationToken cancellationToken)
    {
        FairReturnSupplyPolicy.RequireUtc(
            observedAtUtc,
            nameof(observedAtUtc));
        int pending = await CountAsync(
            FairReturnOrchestrationEffectStatus.Pending,
            cancellationToken);
        int processing = await CountAsync(
            FairReturnOrchestrationEffectStatus
                .Processing,
            cancellationToken);
        int deadLettered = await CountAsync(
            FairReturnOrchestrationEffectStatus
                .DeadLettered,
            cancellationToken);
        int unknown = await dbContext
            .FairReturnOrchestrationEffects
            .CountAsync(value =>
                value.LastFailureCode ==
                    "REPLACEMENT_PAYMENT_UNKNOWN",
                cancellationToken);
        DateTime? oldest = await dbContext
            .FairReturnOrchestrationEffects
            .Where(value =>
                value.StatusId ==
                    (int)
                    FairReturnOrchestrationEffectStatus
                        .Pending)
            .MinAsync(
                value => (DateTime?)value.CreatedAt,
                cancellationToken);
        return new FairReturnOrchestrationHealth(
            pending,
            processing,
            unknown,
            deadLettered,
            oldest);
    }

    private async Task<(
        FairReturnOrchestrationEffect? Effect,
        WaitlistPaymentIntent? Intent)> LockClaimAsync(
            FairReturnOrchestrationClaim claim,
            CancellationToken cancellationToken)
    {
        FairReturnOrchestrationEffect? effect =
            await LockEffectAsync(
                claim,
                cancellationToken);
        if (effect is null)
        {
            return (null, null);
        }
        await FenceAsync<WaitlistPaymentIntent>(
            claim.TenantId,
            claim.WaitlistPaymentIntentId,
            intent => intent.Id,
            cancellationToken);
        WaitlistPaymentIntent? intent =
            await dbContext.WaitlistPaymentIntents
                .SingleOrDefaultAsync(value =>
                    value.TenantId == claim.TenantId
                    && value.Id ==
                        claim.WaitlistPaymentIntentId,
                    cancellationToken);
        return (effect, intent);
    }

    private async Task<FairReturnOrchestrationEffect?>
        LockEffectAsync(
            FairReturnOrchestrationClaim claim,
            CancellationToken cancellationToken)
    {
        await FenceAsync<
            FairReturnOrchestrationEffect>(
                claim.TenantId,
                claim.EffectId,
                effect => effect.Id,
                cancellationToken);
        return await dbContext
            .FairReturnOrchestrationEffects
            .SingleOrDefaultAsync(value =>
                value.TenantId == claim.TenantId
                && value.Id == claim.EffectId,
                cancellationToken);
    }

    private Task<int> CountAsync(
        FairReturnOrchestrationEffectStatus status,
        CancellationToken cancellationToken) =>
        dbContext.FairReturnOrchestrationEffects
            .CountAsync(value =>
                value.StatusId == (int)status,
                cancellationToken);

    private static bool Owns(
        FairReturnOrchestrationEffect? effect,
        FairReturnOrchestrationClaim claim) =>
        effect?.StatusId ==
            (int)FairReturnOrchestrationEffectStatus
                .Processing
        && effect.ProcessingFence ==
            claim.ProcessingFence;

    private static FairReturnOrchestrationEffect[]
        RoundRobin(
            IReadOnlyCollection<
                FairReturnOrchestrationEffect> due,
            int batchSize,
            int maximumEffectsPerTenant)
    {
        Queue<FairReturnOrchestrationEffect>[]
            tenantQueues = due
                .GroupBy(value => value.TenantId)
                .OrderBy(group =>
                    group.Min(value =>
                        value.StableCursor))
                .ThenBy(group => group.Key)
                .Select(group => new Queue<
                    FairReturnOrchestrationEffect>(
                        group
                            .OrderBy(value =>
                                value.StableCursor)
                            .ThenBy(value =>
                                value.Id)
                            .Take(
                                maximumEffectsPerTenant)))
                .ToArray();
        var selected = new List<
            FairReturnOrchestrationEffect>(
                Math.Min(batchSize, due.Count));
        bool added;
        do
        {
            added = false;
            foreach (Queue<
                         FairReturnOrchestrationEffect>
                     queue in tenantQueues)
            {
                if (selected.Count == batchSize)
                {
                    return selected.ToArray();
                }
                if (queue.TryDequeue(out
                        FairReturnOrchestrationEffect?
                            effect))
                {
                    selected.Add(effect);
                    added = true;
                }
            }
        } while (added);
        return selected.ToArray();
    }

    private async Task<T> ExecuteFencedAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction
            is not null)
        {
            return await operation();
        }
        return await new EfCoreUnitOfWork(dbContext)
            .ExecuteInTransactionAsync(
                _ => operation(),
                cancellationToken);
    }

    private Task FenceAsync<TEntity>(
        Guid tenantId,
        Guid id,
        Expression<Func<TEntity, Guid>> keyPropertyExpression,
        CancellationToken cancellationToken)
        where TEntity : class, ITenantEntity =>
        RelationalEntityRowFence.AcquireAsync<TEntity>(
            dbContext,
            tenantId,
            keyPropertyExpression,
            id,
            cancellationToken);
}
