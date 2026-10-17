using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Privacy.ErasureAuthority.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Privacy.ErasureAuthority.Repositories;

public sealed partial class EmbeddedPrivacyErasureAuthorityRepository : IOriginalOwnerSettlementBoundary
{
    private OriginalOwnerSettlement? _settlement;
    private OriginalOwnerSettlement Settlement => _settlement ??= new(applicationContext
        ?? throw new InvalidOperationException("Physical settlement requires the original application context."));

    Task<IOriginalOwnerSettlement<T>> IOriginalOwnerSettlementBoundary.ExecuteAsync<T>(object invocation,
        Func<CancellationToken, Task<OriginalOwnerDecision<T>>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (_identityGate.Value is not null)
            throw new InvalidOperationException("A borrowed identity gate cannot issue physical settlement.");
        return Settlement.ExecuteAsync(invocation, async attempt =>
        {
            await EnsureStorageReadyAsync(cancellationToken);
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            if (!EmbeddedIdentityFenceConnection.TryShareApplicationConnection(applicationContext, db))
                throw new InvalidOperationException("This settlement boundary requires co-located SQLite ownership.");
            await using var transaction =
                await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            await using var enlistment = await applicationContext!.Database.UseTransactionAsync(
                transaction.GetDbTransaction(), cancellationToken);
            applicationContext.IdentityFenceOwnsTransaction = true;
            applicationContext.IdentityFenceTransactionFailed = false;
            _identityGate.Value = db;
            try
            {
                var decision = await Settlement.SettleAsync(attempt, transaction, async token =>
                {
                    _ = await ReadOrInitializeIdentityCounterAsync(db, token);
                    await db.SaveChangesAsync(token);
                    return await operation(token);
                }, cancellationToken);
                storage?.HardenCompanionFiles();
                return decision;
            }
            finally
            {
                _identityGate.Value = null;
                db.ChangeTracker.Clear();
                applicationContext.IdentityFenceOwnsTransaction = false;
                applicationContext.IdentityFenceTransactionFailed = false;
                applicationContext.ResetHumanCommitAdmissions();
                applicationContext.ResetTrustedSetupSettingsAdmissions();
            }
        }, cancellationToken);
    }

    T IOriginalOwnerSettlementBoundary.Consume<T>(object invocation, IOriginalOwnerSettlement<T> settlement,
        OriginalOwnerDisposition expectedDisposition) => Settlement.Consume(invocation, settlement, expectedDisposition);
}
