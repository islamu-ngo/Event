using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Explore.Persistence.Database;

/// <summary>
/// Finalizes accumulated disclosure mutations for native transactions whose owner
/// does not use the application unit of work, including direct repository commits.
/// </summary>
/// <remarks>
/// Creation workflows must use EF-owned transaction APIs for savepoint rollback.
/// Raw provider savepoint methods and raw rollback SQL bypass EF interception and
/// are unsupported by this creation-receipt protocol.
/// </remarks>
internal sealed class EventDiscoveryDisclosureTransactionInterceptor : DbTransactionInterceptor
{
    internal static EventDiscoveryDisclosureTransactionInterceptor Instance { get; } = new();

    public override InterceptionResult RollingBackToSavepoint(
        DbTransaction transaction, TransactionEventData eventData, InterceptionResult result)
    {
        if (eventData.Context is ExploreDbContext context)
            context.InvalidateDiscoveryEnrollmentSavepoint(transaction);
        return result;
    }

    public override ValueTask<InterceptionResult> RollingBackToSavepointAsync(
        DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        // Invalidate before attempting rollback, even if rollback subsequently
        // fails or is cancelled. Only the whole original attempt may recover.
        if (eventData.Context is ExploreDbContext context)
            context.InvalidateDiscoveryEnrollmentSavepoint(transaction);
        return ValueTask.FromResult(result);
    }

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
