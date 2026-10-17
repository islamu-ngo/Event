using System.Data;
using Explore.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence;

public sealed partial class EfCoreUnitOfWork : IOriginalOwnerSettlementBoundary
{
    private OriginalOwnerSettlement? _settlement;
    private OriginalOwnerSettlement Settlement => _settlement ??= new(_dbContext);

    Task<IOriginalOwnerSettlement<T>> IOriginalOwnerSettlementBoundary.ExecuteAsync<T>(object invocation,
        Func<CancellationToken, Task<OriginalOwnerDecision<T>>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return Settlement.ExecuteAsync(invocation, async attempt =>
        {
            await using var transaction =
                await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            return await Settlement.SettleAsync(attempt, transaction, operation, cancellationToken);
        }, cancellationToken);
    }

    T IOriginalOwnerSettlementBoundary.Consume<T>(object invocation, IOriginalOwnerSettlement<T> settlement,
        OriginalOwnerDisposition expectedDisposition) => Settlement.Consume(invocation, settlement, expectedDisposition);
}
