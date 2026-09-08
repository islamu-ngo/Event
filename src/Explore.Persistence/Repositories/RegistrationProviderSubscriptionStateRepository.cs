using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class RegistrationProviderSubscriptionStateRepository(ExploreDbContext dbContext)
    : IRegistrationProviderSubscriptionStateRepository
{
    public Task<RegistrationProviderSubscriptionState?> GetAsync(
        Guid tenantId,
        Guid registrationProviderBindingId,
        string providerEventType,
        CancellationToken cancellationToken) => dbContext.RegistrationProviderSubscriptionStates
        .SingleOrDefaultAsync(value => value.TenantId == tenantId &&
            value.RegistrationProviderBindingId == registrationProviderBindingId &&
            value.ProviderEventType == providerEventType, cancellationToken);

    public Task<IReadOnlyList<RegistrationProviderSubscriptionState>> ClaimDueRenewalsAsync(
        int batchSize,
        DateTime renewBefore,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken) => ClaimAsync(
        "registration-provider-subscription-state-claim",
        rows => rows.Where(value =>
            value.WatchExpiresAt <= renewBefore &&
            value.FailureCategory != RegistrationProviderSubscriptionState.RenewalInDoubtFailureCategory &&
            value.FailureCategory != RegistrationProviderSubscriptionState.RenewalRejectedFailureCategory &&
            (value.NextRenewalAttemptAt == null || value.NextRenewalAttemptAt <= claimedAt)),
        rows => rows.OrderBy(value => value.WatchExpiresAt).ThenBy(value => value.Id),
        batchSize,
        claimedAt,
        leaseDuration,
        cancellationToken);

    public Task<IReadOnlyList<RegistrationProviderSubscriptionState>> ClaimDueSweepsAsync(
        int batchSize,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken) => ClaimAsync(
        "registration-provider-subscription-state-claim",
        rows => rows.Where(value => value.PendingNotificationAt != null && (value.NextSweepAttemptAt == null || value.NextSweepAttemptAt <= claimedAt) ||
            value.PendingNotificationAt == null && value.NextSweepAttemptAt != null && value.NextSweepAttemptAt <= claimedAt),
        rows => rows.OrderBy(value => value.PendingNotificationAt ?? value.NextSweepAttemptAt).ThenBy(value => value.Id),
        batchSize,
        claimedAt,
        leaseDuration,
        cancellationToken);

    public async Task<IReadOnlyList<RegistrationProviderSubscriptionState>> GetExpiringAsync(
        DateTime expiresBefore,
        int limit,
        CancellationToken cancellationToken)
    {
        if (expiresBefore.Kind != DateTimeKind.Utc || limit is < 1 or > 1000) return [];
        return await dbContext.RegistrationProviderSubscriptionStates
            .AsNoTracking()
            .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationProviderSubscriptionStateWorkerCrossTenantQueue)
            .Where(value => value.WatchExpiresAt <= expiresBefore)
            .OrderBy(value => value.WatchExpiresAt)
            .ThenBy(value => value.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(RegistrationProviderSubscriptionState state, CancellationToken cancellationToken) =>
        await dbContext.RegistrationProviderSubscriptionStates.AddAsync(state, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    private async Task<IReadOnlyList<RegistrationProviderSubscriptionState>> ClaimAsync(
        string lockName,
        Func<IQueryable<RegistrationProviderSubscriptionState>, IQueryable<RegistrationProviderSubscriptionState>> filter,
        Func<IQueryable<RegistrationProviderSubscriptionState>, IOrderedQueryable<RegistrationProviderSubscriptionState>> order,
        int batchSize,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        if (batchSize is < 1 or > 1000 || claimedAt.Kind != DateTimeKind.Utc || leaseDuration <= TimeSpan.Zero) return [];

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            if (dbContext.Database.IsRelational())
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
                IReadOnlyList<RegistrationProviderSubscriptionState> rows = await TryClaimRowsAsync(lockName, filter, order, batchSize, claimedAt, leaseDuration, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return rows;
            }

            return await TryClaimRowsAsync(lockName, filter, order, batchSize, claimedAt, leaseDuration, cancellationToken);
        });
    }

    private async Task<IReadOnlyList<RegistrationProviderSubscriptionState>> TryClaimRowsAsync(
        string lockName,
        Func<IQueryable<RegistrationProviderSubscriptionState>, IQueryable<RegistrationProviderSubscriptionState>> filter,
        Func<IQueryable<RegistrationProviderSubscriptionState>, IOrderedQueryable<RegistrationProviderSubscriptionState>> order,
        int batchSize,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        await using IAsyncDisposable claimLock =
            await RelationalNamedLock.AcquireTransactionAsync(
                dbContext,
                lockName,
                cancellationToken);

        IQueryable<RegistrationProviderSubscriptionState> claimable = dbContext.RegistrationProviderSubscriptionStates
            .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationProviderSubscriptionStateWorkerCrossTenantQueue)
            .Where(value => value.LeaseExpiresAt == null || value.LeaseExpiresAt <= claimedAt);
        List<RegistrationProviderSubscriptionState> rows = await order(filter(claimable))
            .Take(batchSize)
            .ToListAsync(cancellationToken);
        foreach (RegistrationProviderSubscriptionState row in rows)
        {
            row.Claim(Guid.CreateVersion7(), claimedAt.Add(leaseDuration), claimedAt);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return rows;
        }
        catch (DbUpdateConcurrencyException)
        {
            foreach (RegistrationProviderSubscriptionState row in rows)
            {
                dbContext.Entry(row).State = EntityState.Detached;
            }

            return [];
        }
    }
}
