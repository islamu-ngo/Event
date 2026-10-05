using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Explore.Persistence.Database;

/// <summary>
/// Finalizes accumulated disclosure mutations for native transactions whose owner
/// does not use the application unit of work, including direct repository commits.
/// </summary>
internal sealed class EventDiscoveryDisclosureTransactionInterceptor : DbTransactionInterceptor
{
    internal static EventDiscoveryDisclosureTransactionInterceptor Instance { get; } = new();

    public override InterceptionResult TransactionCommitting(
        DbTransaction transaction, TransactionEventData eventData, InterceptionResult result)
    {
        if (eventData.Context is ExploreDbContext context)
            context.FlushDisclosureAsync(CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is ExploreDbContext context)
            await context.FlushDisclosureAsync(cancellationToken);
        return result;
    }
}
