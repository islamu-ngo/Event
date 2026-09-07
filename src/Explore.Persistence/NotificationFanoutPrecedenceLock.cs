using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence;

internal static class NotificationFanoutPrecedenceLock
{
    public static Task<IAsyncDisposable> AcquireAsync(
        ExploreDbContext dbContext,
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        EnsureActiveTransaction(dbContext);
        if (tenantId == Guid.Empty || eventId == Guid.Empty)
        {
            throw new ArgumentException("Fanout precedence locking requires non-empty tenant and event identifiers.");
        }

        return RelationalNamedLock.AcquireTransactionAsync(
            dbContext,
            $"notification-fanout-precedence:{tenantId:N}:{eventId:N}",
            cancellationToken);
    }

    public static void EnsureActiveTransaction(ExploreDbContext dbContext)
    {
        if (!dbContext.Database.IsRelational() || dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Fanout precedence operations require an active relational unit-of-work transaction.");
        }
    }
}
