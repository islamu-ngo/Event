using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

/// <summary>Global claiming cannot read payment, order, or fulfillment graphs.</summary>
public sealed class RegistrationFinalizationQueueRepository(IGlobalExploreDbContextFactory globalFactory)
    : IRegistrationFinalizationQueueRepository
{
    public async Task<IReadOnlyList<RegistrationFinalizationClaim>> ClaimDueAsync(
        string leaseOwner,
        int batchSize,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(leaseOwner) ||
            leaseOwner.Trim().Length > RegistrationFinalizationEffect.MaxLeaseOwnerLength ||
            batchSize is < 1 or > 1000 || leaseDuration <= TimeSpan.Zero || claimedAt.Kind != DateTimeKind.Utc)
            return [];

        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        var native = await RegistrationFinalizationNativeClaim.TryClaimAsync(
            context, leaseOwner, batchSize, claimedAt, leaseDuration, cancellationToken);
        if (native is not null) return native;

        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await using IAsyncDisposable claimLock = await RelationalNamedLock.AcquireTransactionAsync(
                context, "registration-finalization-claim", cancellationToken);
            var effects = await context.RegistrationFinalizationEffects
                .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationFinalizationWorkerCrossTenantQueue)
                .Where(value =>
                    ((value.Status == OutboxMessageStatus.Pending || value.Status == OutboxMessageStatus.Failed) &&
                     (value.NextAttemptAt == null || value.NextAttemptAt <= claimedAt)) ||
                    (value.Status == OutboxMessageStatus.Processing && value.ProcessingLeaseExpiresAt <= claimedAt))
                .OrderBy(value => value.NextAttemptAt ?? value.CreatedAt)
                .ThenBy(value => value.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
            List<RegistrationFinalizationClaim> claims = new(effects.Count);
            foreach (var effect in effects)
            {
                if (effect.Status == OutboxMessageStatus.Processing)
                    effect.RecoverExpiredClaim(claimedAt);
                Guid leaseToken = Guid.CreateVersion7();
                effect.Claim(leaseOwner, leaseToken, claimedAt.Add(leaseDuration), claimedAt);
                claims.Add(new(effect.Id, effect.TenantId, effect.RegistrationOrderId, leaseToken, effect.ProcessingFence));
            }
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return claims;
        });
    }
}
