using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class RegistrationProviderSubscriptionStateRepository(ExploreDbContext dbContext, TimeProvider? timeProvider = null)
    : IRegistrationProviderSubscriptionCustodyRepository
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task<RegistrationProviderSubscriptionState?> GetActiveClaimAsync(
        RegistrationProviderSubscriptionClaim claim, CancellationToken cancellationToken)
    {
        var state = await ActiveClaim(claim).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return state is not null && claim.LeaseExpiresAt > _clock.GetUtcNow().UtcDateTime ? state : null;
    }

    public async Task<bool> IsCurrentAsync(
        RegistrationProviderSubscriptionClaim claim,
        RegistrationProviderBinding? binding,
        CancellationToken cancellationToken)
    {
        IQueryable<RegistrationProviderSubscriptionState> rows = ActiveClaim(claim);
        if (binding is not null)
        {
            if (binding.TenantId != claim.TenantId || binding.Id != claim.RegistrationProviderBindingId ||
                binding.Connection is null || binding.Connection.TenantId != claim.TenantId)
                return false;
            Guid connectionId = binding.Connection.Id;
            Guid bindingStamp = binding.ConcurrencyStamp;
            Guid connectionStamp = binding.Connection.ConcurrencyStamp;
            rows = rows.Where(state => state.Binding != null &&
                state.Binding.ConcurrencyStamp == bindingStamp &&
                state.Binding.RegistrationProviderConnectionId == connectionId &&
                state.Binding.Connection != null && state.Binding.Connection.ConcurrencyStamp == connectionStamp);
        }
        bool current = await rows.AsNoTracking().AnyAsync(cancellationToken);
        return current && claim.LeaseExpiresAt > _clock.GetUtcNow().UtcDateTime;
    }

    public async Task<RegistrationProviderSubscriptionState> FenceAsync(
        RegistrationProviderSubscriptionClaim claim,
        RegistrationProviderBinding? binding,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Subscription custody fencing requires its local transaction.");
        await using var claimLock = await RelationalNamedLock.AcquireTransactionAsync(
            dbContext, "registration-provider-subscription-state-claim", cancellationToken);
        // Match admission's claim-lock-before-state order. A no-op update takes a native
        // row lock without changing the original token, generation, expiry or row version.
        if (await ActiveClaim(claim).ExecuteUpdateAsync(
                setters => setters.SetProperty(state => state.ConcurrencyStamp, state => state.ConcurrencyStamp),
                cancellationToken) != 1 || !await IsCurrentAsync(claim, binding, cancellationToken))
            throw new RegistrationProviderSubscriptionClaimLostException();

        var tracked = dbContext.ChangeTracker.Entries<RegistrationProviderSubscriptionState>()
            .SingleOrDefault(entry => entry.Entity.Id == claim.StateId);
        if (tracked is not null)
            tracked.State = EntityState.Detached;
        var state = await ActiveClaim(claim).SingleOrDefaultAsync(cancellationToken);
        if (state is null || claim.LeaseExpiresAt <= _clock.GetUtcNow().UtcDateTime)
            throw new RegistrationProviderSubscriptionClaimLostException();
        return state;
    }

    private IQueryable<RegistrationProviderSubscriptionState> ActiveClaim(RegistrationProviderSubscriptionClaim claim)
    {
        bool selected = claim.TenantId != Guid.Empty && claim.LeaseToken != Guid.Empty &&
            claim.ProcessingGeneration > 0 && claim.LeaseExpiresAt.Kind == DateTimeKind.Utc &&
            !dbContext.IsTenantFilterBypassed && dbContext.BindTenantLease() == claim.TenantId;
        DateTime now = _clock.GetUtcNow().UtcDateTime;
        return dbContext.RegistrationProviderSubscriptionStates.Where(state => selected &&
            state.Id == claim.StateId && state.TenantId == claim.TenantId &&
            state.RegistrationProviderBindingId == claim.RegistrationProviderBindingId &&
            state.LeaseToken == claim.LeaseToken && state.ProcessingGeneration == claim.ProcessingGeneration &&
            state.LeaseExpiresAt == claim.LeaseExpiresAt && state.LeaseExpiresAt > now);
    }

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
