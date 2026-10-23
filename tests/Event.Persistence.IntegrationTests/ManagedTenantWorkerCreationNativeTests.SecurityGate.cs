using System.Data.Common;
using Explore.Persistence;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests;

public sealed partial class ManagedTenantWorkerCreationNativeTests
{
    [Test]
    public async Task OriginalGlobalWorkerHoldsHumanCustodyBeforeSourceReadAndThroughSettlement()
    {
        var probe = new WorkerHumanGateProbe();
        await using var scenario = await CreateWorkflowAsync(probe);
        var scheduled = await ScheduleOriginalAsync(scenario);
        var claim = await scenario.Host.ClaimManagedWorkerAsync(scheduled.Id, scenario.Token);
        probe.Armed = true;

        await Assert.That(await scenario.Host.ProcessManagedWorkerAsync(claim, scenario.Token)).IsTrue();

        await Assert.That(probe.ObservedIdleSourceRead).IsTrue();
        await Assert.That(probe.ObservedBeforeBegin).IsTrue();
        await Assert.That(probe.ObservedCommit).IsTrue();
        await Assert.That(HumanSecuritySourceGate.TryRequireCurrent(probe.OriginalContext!)).IsFalse();
    }

    /// <summary>
    /// Observes the real admitted creator, not Runtime claim reads or Migrator setup.
    /// Its idle operation observation precedes the optional Local eligibility branch.
    /// </summary>
    private sealed class WorkerHumanGateProbe : DbCommandInterceptor, IDbTransactionInterceptor
    {
        internal bool Armed { get; set; }
        internal bool ObservedIdleSourceRead { get; private set; }
        internal bool ObservedBeforeBegin { get; private set; }
        internal bool ObservedCommit { get; private set; }
        internal ExploreDbContext? OriginalContext { get; private set; }
        private DbConnection? connector;

        private bool IsCreator(DbContext? candidate) => Armed && candidate is ExploreDbContext context
            && context.HasOriginalCommitContextLease && !context.HasHumanCommitContextLease;

        private void RequireOriginal(ExploreDbContext context, DbConnection connection)
        {
            HumanSecuritySourceGate.RequireCurrent(context);
            if (context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
                || OriginalContext is not null && (!ReferenceEquals(context, OriginalContext)
                    || !ReferenceEquals(connection, connector)))
                throw new InvalidOperationException("worker_test_human_gate_owner_changed");
            OriginalContext ??= context;
            connector ??= connection;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (IsCreator(eventData.Context))
            {
                var context = (ExploreDbContext)eventData.Context!;
                RequireOriginal(context, command.Connection!);
                if (!ObservedIdleSourceRead)
                {
                    if (context.Database.CurrentTransaction is not null)
                        throw new InvalidOperationException("worker_test_first_source_observed_after_begin");
                    ObservedIdleSourceRead = true;
                }
            }
            return ValueTask.FromResult(result);
        }

        public ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (IsCreator(eventData.Context))
            {
                RequireOriginal((ExploreDbContext)eventData.Context!, connection);
                if (!ObservedIdleSourceRead)
                    throw new InvalidOperationException("worker_test_original_source_not_observed");
                ObservedBeforeBegin = true;
            }
            return ValueTask.FromResult(result);
        }

        public ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (IsCreator(eventData.Context))
            {
                var context = (ExploreDbContext)eventData.Context!;
                RequireOriginal(context, transaction.Connection!);
                if (!ObservedBeforeBegin
                    || !ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), transaction))
                    throw new InvalidOperationException("worker_test_original_settlement_changed");
                ObservedCommit = true;
            }
            return ValueTask.FromResult(result);
        }
    }
}
