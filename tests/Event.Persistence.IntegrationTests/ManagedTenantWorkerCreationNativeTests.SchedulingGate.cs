using System.Data;
using System.Data.Common;
using Explore.Application.DTOs.Management;
using Explore.Application.Features.Management;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests;

public sealed partial class ManagedTenantWorkerCreationNativeTests
{
    [Test]
    public async Task OriginalGlobalSchedulerTakesHumanBeforeLocalAndRetainsBothThroughSettlement()
    {
        var probe = new SchedulingHumanGateProbe(fixture.NativeQueueEnrollment.GlobalLogin);
        await using var scenario = await CreateWorkflowAsync(probe);
        var request = ManagedTenantProvisioningRequestCodec.Normalize(scenario.Request with
        {
            Administrator = new ManagementTenantAdministratorDto
            {
                LocalIdentity = new ManagementTenantLocalIdentityDto { LocalSubjectId = scenario.Host.ActorId }
            }
        });
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(scenario.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        await using var holder = OpenMigrator();
        await holder.Database.OpenConnectionAsync(deadline.Token);
        await using var human = new HumanSecuritySourceGate(holder);
        await using var held = await human.AcquireAsync(deadline.Token);
        int holderBackend = ((NpgsqlConnection)holder.Database.GetDbConnection()).ProcessID;
        await using var observer = OpenMigrator();
        await observer.Database.OpenConnectionAsync(deadline.Token);
        probe.Armed = true;
        var scheduling = scenario.Host.ScheduleManagedWorkerAsync(scenario.ManagedInstanceId, request, deadline.Token);
        bool completed = false;
        try
        {
            int waitingBackend = await WaitForSchedulingHumanWaitAsync(observer, holderBackend,
                fixture.NativeQueueEnrollment.GlobalLogin, scheduling, deadline.Token);
            probe.ExpectedBackend = waitingBackend;
            human.RequireCurrent();
            await Assert.That(probe.ObservedSource).IsFalse();
            await Assert.That(probe.ObservedBegin).IsFalse();

            // A scheduler waiting for Human must not already hold the later Local resource.
            long localKey = RelationalNamedLock.ComputeStableKey("local-identity-current-eligibility");
            await using (var localProbe = observer.Database.GetDbConnection().CreateCommand())
            {
                localProbe.CommandText = "SELECT pg_catalog.pg_try_advisory_lock(@key)";
                localProbe.Parameters.Add(new NpgsqlParameter("key", localKey));
                bool acquired = await localProbe.ExecuteScalarAsync(deadline.Token) is true;
                try { await Assert.That(acquired).IsTrue(); }
                finally
                {
                    if (acquired)
                    {
                        localProbe.CommandText = "SELECT pg_catalog.pg_advisory_unlock(@key)";
                        await Assert.That(await localProbe.ExecuteScalarAsync(CancellationToken.None)).IsEqualTo(true);
                    }
                }
            }

            await held.DisposeAsync();
            var response = await scheduling;
            completed = true;
            await Assert.That(response.IsSuccess).IsTrue();
            await Assert.That(response.Id).IsNotNull();
            await Assert.That(probe.ObservedSource).IsTrue();
            await Assert.That(probe.ObservedBegin).IsTrue();
            await Assert.That(probe.ObservedCommit).IsTrue();
            await Assert.That(HumanSecuritySourceGate.TryRequireCurrent(probe.OriginalContext!)).IsFalse();

            var operation = await observer.ManagedTenantProvisioningOperations.AsNoTracking()
                .SingleAsync(row => row.Id == response.Id!.OperationId, deadline.Token);
            await Assert.That(operation.Status).IsEqualTo(ManagedTenantProvisioningStatus.Pending);
            await Assert.That(operation.StartedAt).IsNull();
            await Assert.That(operation.RequestHash).IsEqualTo(ManagedTenantProvisioningRequestCodec.ComputeHash(request));
            var message = await observer.Set<OutboxMessage>().AsNoTracking()
                .SingleAsync(row => row.Id == operation.CurrentOutboxMessageId, deadline.Token);
            await Assert.That(message.AggregateId).IsEqualTo(operation.Id);
            await Assert.That(message.EventType).IsEqualTo("ManagedTenantProvisioningProcessRequested");
            await Assert.That(message.Status).IsEqualTo(OutboxMessageStatus.Pending);

            // Retirement releases both real resources, not merely the in-memory custody flag.
            await using var reacquiredHuman = await new HumanSecuritySourceGate(observer).AcquireAsync(deadline.Token);
            await using var reacquiredLocal = await LocalIdentityLifecycleGateCustody.EnterAsync(observer, deadline.Token);
            HumanSecuritySourceGate.RequireCurrent(observer);
            reacquiredLocal.RequireCurrent();
        }
        finally
        {
            if (!completed) deadline.Cancel();
            await held.DisposeAsync();
            if (!completed)
            {
                try { await scheduling; }
                catch (Exception) { /* Preserve the original assertion/failure after joining the owned request. */ }
            }
        }
    }

    private static async Task<int> WaitForSchedulingHumanWaitAsync(ExploreDbContext observer,
        int holderBackend, string globalLogin, Task scheduling, CancellationToken token)
    {
        await using var command = observer.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT waiting.pid
            FROM pg_catalog.pg_locks waiting
            JOIN pg_catalog.pg_stat_activity activity ON activity.pid=waiting.pid
            WHERE waiting.locktype='advisory' AND NOT waiting.granted
                AND activity.usename=@login
                AND waiting.database=(SELECT oid FROM pg_catalog.pg_database WHERE datname=current_database())
                AND @holder=ANY(pg_catalog.pg_blocking_pids(waiting.pid))
            """;
        command.Parameters.Add(new NpgsqlParameter("login", globalLogin));
        command.Parameters.Add(new NpgsqlParameter("holder", holderBackend));
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (scheduling.IsCompleted)
            {
                await scheduling;
                throw new InvalidOperationException("The scheduler completed without waiting for original Human custody.");
            }
            if (await command.ExecuteScalarAsync(token) is int backend) return backend;
            await Task.WhenAny(scheduling, Task.Delay(TimeSpan.FromMilliseconds(25), token)).WaitAsync(token);
        }
    }

    private sealed class SchedulingHumanGateProbe(string globalLogin) : DbCommandInterceptor, IDbTransactionInterceptor
    {
        internal bool Armed { get; set; }
        internal int? ExpectedBackend { get; set; }
        internal bool ObservedSource { get; private set; }
        internal bool ObservedBegin { get; private set; }
        internal bool ObservedCommit { get; private set; }
        internal ExploreDbContext? OriginalContext { get; private set; }
        private DbConnection? originalConnection;
        private DbTransaction? originalTransaction;

        private bool IsScheduler(DbContext? context, DbConnection? connection) => Armed
            && context is ExploreDbContext && connection is NpgsqlConnection postgres
            && new NpgsqlConnectionStringBuilder(postgres.ConnectionString).Username == globalLogin;

        private void RequireOriginal(ExploreDbContext context, DbConnection connection)
        {
            context.RequireBoundTenantLease();
            HumanSecuritySourceGate.RequireCurrent(context);
            LocalIdentityLifecycleGateCustody.RequireHeld(context);
            if (context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
                || ExpectedBackend is not { } backend || ((NpgsqlConnection)connection).ProcessID != backend
                || OriginalContext is not null && (!ReferenceEquals(context, OriginalContext)
                    || !ReferenceEquals(connection, originalConnection)))
                throw new InvalidOperationException("scheduling_test_original_owner_changed");
            OriginalContext ??= context;
            originalConnection ??= connection;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (IsScheduler(eventData.Context, command.Connection))
            {
                var context = (ExploreDbContext)eventData.Context!;
                RequireOriginal(context, command.Connection!);
                if (!ObservedSource && context.Database.CurrentTransaction is not null)
                    throw new InvalidOperationException("scheduling_test_first_source_after_begin");
                ObservedSource = true;
            }
            return ValueTask.FromResult(result);
        }

        public ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (IsScheduler(eventData.Context, connection))
            {
                RequireOriginal((ExploreDbContext)eventData.Context!, connection);
                if (!ObservedSource) throw new InvalidOperationException("scheduling_test_source_not_observed");
                ObservedBegin = true;
            }
            return ValueTask.FromResult(result);
        }

        public ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
            TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
        {
            if (IsScheduler(eventData.Context, connection))
            {
                RequireOriginal((ExploreDbContext)eventData.Context!, connection);
                if (originalTransaction is not null || result.IsolationLevel != IsolationLevel.Serializable)
                    throw new InvalidOperationException("scheduling_test_original_transaction_changed");
                originalTransaction = result;
            }
            return ValueTask.FromResult(result);
        }

        public ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (IsScheduler(eventData.Context, transaction.Connection))
            {
                var context = (ExploreDbContext)eventData.Context!;
                RequireOriginal(context, transaction.Connection!);
                if (!ReferenceEquals(transaction, originalTransaction)
                    || !ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), transaction))
                    throw new InvalidOperationException("scheduling_test_original_settlement_changed");
                ObservedCommit = true;
            }
            return ValueTask.FromResult(result);
        }
    }
}
