using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Testing;

/// <summary>Fails cleanup only after the original EF transaction has really disposed.</summary>
internal sealed class TransactionCleanupFault(bool loseAcknowledgement, Exception cleanupFailure)
    : DbTransactionInterceptor
{
    private static readonly ConditionalWeakTable<DbContext, TransactionCleanupFault> Faults = new();
    internal bool PhysicalCommitObserved { get; private set; }
    internal int DisposalFailures;
    private DbTransaction? targetTransaction;
    internal IOException CommitFailure { get; } = new("Lost acknowledgement after physical commit.");
    internal Exception CleanupFailure { get; } = cleanupFailure;

    internal void Arm(DbContext context, DbTransaction? transaction = null)
    {
        targetTransaction = transaction;
        Faults.Add(context, this);
    }

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not { } context || !Faults.TryGetValue(context, out var fault)
            || !ReferenceEquals(fault, this)
            || targetTransaction is not null && !ReferenceEquals(targetTransaction, transaction))
            return Task.CompletedTask;
        PhysicalCommitObserved = true;
        if (loseAcknowledgement) throw CommitFailure;
        return Task.CompletedTask;
    }

    public sealed class Factory(RelationalTransactionFactoryDependencies dependencies)
        : RelationalTransactionFactory(dependencies)
    {
        public override RelationalTransaction Create(IRelationalConnection connection, DbTransaction transaction,
            Guid transactionId, IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger, bool transactionOwned)
        {
            return new FaultingTransaction(connection, transaction, transactionId, logger,
                transactionOwned, Dependencies.SqlGenerationHelper, connection.Context);
        }

        private sealed class FaultingTransaction(IRelationalConnection connection, DbTransaction transaction,
            Guid transactionId, IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger,
            bool transactionOwned, ISqlGenerationHelper sqlGenerationHelper, DbContext context)
            : RelationalTransaction(connection, transaction, transactionId, logger, transactionOwned, sqlGenerationHelper)
        {
            public override async ValueTask DisposeAsync()
            {
                DbTransaction originalTransaction = this.GetDbTransaction();
                await base.DisposeAsync();
                if (Faults.TryGetValue(context, out var fault)
                    && (fault.targetTransaction is null || ReferenceEquals(fault.targetTransaction, originalTransaction))
                    && Interlocked.Exchange(ref fault.DisposalFailures, 1) == 0)
                    throw fault.CleanupFailure;
            }
        }
    }
}
