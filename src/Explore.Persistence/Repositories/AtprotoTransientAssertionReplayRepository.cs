// ABOUTME: Persists instance-wide replay claims for private ATProto transient-service assertions.
// ABOUTME: Uses insert-only unique claims and bounded integer-expiry cleanup across relational providers.

using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
namespace Explore.Persistence.Repositories;

public sealed class AtprotoTransientAssertionReplayRepository(ExploreDbContext dbContext, TimeProvider timeProvider)
    : IAtprotoTransientAssertionReplayRepository
{
    private const int MaximumDeleteBatchSize = 500;

    public async Task<bool> TryClaimAsync(AtprotoTransientAssertionReplay replay, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replay);
        EnsureRelational();
        if (dbContext.Database.CurrentTransaction is not null
            || System.Transactions.Transaction.Current is not null
            || dbContext.GetService<IDbContextTransactionManager>() is ITransactionEnlistmentManager { EnlistedTransaction: not null })
        {
            throw new InvalidOperationException("ATProto assertion replay claims own their commit boundary and reject outer transactions.");
        }
        if (replay.ExpiresAtUnixMilliseconds <= timeProvider.GetUtcNow().ToUnixTimeMilliseconds())
        {
            return false;
        }
        dbContext.AtprotoTransientAssertionReplays.Add(replay);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return replay.ExpiresAtUnixMilliseconds > timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        }
        catch (DbUpdateException exception) when (AtprotoTransientUniqueConflictClassifier.IsAssertionReplayConflict(dbContext, exception))
        {
            dbContext.Entry(replay).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<int> DeleteExpiredAsync(long expiresAtOrBeforeUnixMilliseconds, int batchSize, CancellationToken cancellationToken = default)
    {
        EnsureRelational();
        if (batchSize is < 1 or > MaximumDeleteBatchSize) throw new ArgumentOutOfRangeException(nameof(batchSize));
        Guid[] ids = await dbContext.AtprotoTransientAssertionReplays.AsNoTracking().Where(replay =>
            replay.ExpiresAtUnixMilliseconds <= expiresAtOrBeforeUnixMilliseconds)
            .OrderBy(replay => replay.ExpiresAtUnixMilliseconds).ThenBy(replay => replay.Id)
            .Select(replay => replay.Id).Take(batchSize).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return await AtprotoTransientCleanupDelete.ExecuteAsync<AtprotoTransientAssertionReplay>(dbContext, ids, cancellationToken)
            .ConfigureAwait(false);
    }

    private void EnsureRelational()
    {
        if (!dbContext.Database.IsRelational()) throw new InvalidOperationException("ATProto assertion replay storage requires a relational provider.");
    }
}
