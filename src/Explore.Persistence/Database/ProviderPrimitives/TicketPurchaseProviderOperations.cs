using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Database;

internal static class TicketPurchaseProviderOperations
{
    public static async Task<T> ExecuteSerializableAsync<T>(
        ExploreDbContext dbContext,
        IReadOnlyList<string> lockScopes,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(lockScopes);
        ArgumentNullException.ThrowIfNull(operation);

        IExecutionStrategy strategy =
            dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var leases = new List<IAsyncDisposable>(
                lockScopes.Count);
            try
            {
                foreach (string scope in lockScopes)
                {
                    leases.Add(
                        await RelationalNamedLock
                            .AcquireSessionAsync(
                                dbContext,
                                scope,
                                cancellationToken));
                }

                await using IDbContextTransaction transaction =
                    await dbContext.Database
                        .BeginTransactionAsync(
                            IsolationLevel.Serializable,
                            cancellationToken);
                T result = await operation(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            finally
            {
                for (int index = leases.Count - 1;
                    index >= 0;
                    index--)
                {
                    await leases[index].DisposeAsync();
                }
            }
        });
    }
}
