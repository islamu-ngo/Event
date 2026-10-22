using System.Data.Common;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Event.Persistence.IntegrationTests.Federation;

public sealed partial class EventDiscoveryEnrollmentProducerTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SelectedTenantSourceWriterWaitsBeforeBeginAndKeepsCustodyThroughSettlement(bool bulk)
    {
        await using var store = await Store.CreateAsync();
        var observation = new LocalWriterObservation();
        await using var writer = store.Open(store.TenantId,
            new LocalWriterTransactionObserver(observation), new LocalWriterCommandObserver(observation));
        var tenant = await writer.Tenants.SingleAsync(row => row.Id == store.TenantId);
        string originalName = tenant.FullName;
        observation.Bind(writer, store.TenantId);
        if (!bulk)
            tenant.FullName = "Coordinated tenant-local change";

        await using var holder = store.Open(store.TenantId);
        await using var held = await new EventDiscoveryEnrollmentGate(holder).AcquireAsync();
        await using var contender = store.Open(store.TenantId);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task<IAsyncDisposable>? competing = null;
        Task<bool> writing = WriteAsync();
        bool beganWhileHeld;
        int commandsWhileHeld;
        bool completedWhileHeld;
        bool competingBlockedAtSettlement;
        try
        {
            // A real competing lease, not a provider row lock, is the obstacle.
            // Any original transaction/source command before its release violates ordering.
            await Task.WhenAny(observation.Begin.Task, Task.Delay(TimeSpan.FromMilliseconds(150), cancellation.Token));
            beganWhileHeld = observation.Begin.Task.IsCompleted;
            commandsWhileHeld = observation.Commands;
            completedWhileHeld = writing.IsCompleted;
            await held.DisposeAsync();

            await Task.WhenAny(observation.Committing.Task, writing)
                .WaitAsync(TimeSpan.FromSeconds(5), cancellation.Token);
            if (writing.IsCompleted)
                await writing;
            await observation.Committing.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellation.Token);

            competing = new EventDiscoveryEnrollmentGate(contender).AcquireAsync(cancellation.Token);
            await Task.WhenAny(competing, Task.Delay(TimeSpan.FromMilliseconds(150), cancellation.Token));
            competingBlockedAtSettlement = !competing.IsCompleted;
            observation.AllowCommit.TrySetResult();
            await Assert.That(await writing).IsTrue();
            await using var subsequent = await competing.WaitAsync(TimeSpan.FromSeconds(5), cancellation.Token);
        }
        finally
        {
            observation.AllowCommit.TrySetResult();
            await held.DisposeAsync();
            cancellation.Cancel();
            try
            {
                await writing;
            }
            finally
            {
                if (competing is not null)
                {
                    try
                    {
                        await using var remaining = await competing;
                    }
                    catch (OperationCanceledException)
                    {
                        // The losing genuine gate releases its own connector and wait.
                    }
                }
            }
        }

        await Assert.That(observation.OriginalOwnerPreserved).IsTrue();
        await Assert.That(writer.Database.CurrentTransaction).IsNull();
        await Assert.That(EventDiscoveryEnrollmentGate.TryRequireCurrent(writer)).IsFalse();

        await using var reader = store.Open(store.TenantId);
        var committed = await reader.Tenants.AsNoTracking().SingleAsync(row => row.Id == store.TenantId);
        var revision = await reader.Set<EventDiscoveryRevision>().AsNoTracking().SingleAsync();
        await Assert.That(committed.FullName).IsEqualTo(bulk ? originalName : "Coordinated tenant-local change");
        await Assert.That(committed.TenantStatusId)
            .IsEqualTo((int)(bulk ? TenantStatusEnum.Suspended : TenantStatusEnum.Active));
        await Assert.That(revision.Id).IsEqualTo(store.RevisionId);
        await Assert.That(revision.TenantId).IsEqualTo(store.TenantId);
        await Assert.That(revision.IdentityEpoch).IsEqualTo(7);
        await Assert.That(revision.DisclosureEpoch).IsEqualTo(12);
        await Assert.That(beganWhileHeld).IsFalse();
        await Assert.That(commandsWhileHeld).IsEqualTo(0);
        await Assert.That(completedWhileHeld).IsFalse();
        await Assert.That(observation.HeldBeforeBegin).IsTrue();
        await Assert.That(observation.HeldBeforeCommit).IsTrue();
        await Assert.That(observation.HeldAfterCommit).IsTrue();
        await Assert.That(competingBlockedAtSettlement).IsTrue();

        async Task<bool> WriteAsync() => bulk
            ? await new TenantRepository(writer).TryTransitionStatusAsync(
                store.TenantId, (int)TenantStatusEnum.Active, (int)TenantStatusEnum.Suspended,
                DateTime.UtcNow, Guid.CreateVersion7(), cancellation.Token)
            : await writer.SaveChangesAsync(cancellation.Token) > 0;
    }

    private sealed class LocalWriterObservation
    {
        private ExploreDbContext? original;
        private DbConnection? connector;
        private DbContextId contextId;
        private Guid tenantId;
        private int commands;
        internal TaskCompletionSource Begin { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Committing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource AllowCommit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Commands => Volatile.Read(ref commands);
        internal bool HeldBeforeBegin { get; set; }
        internal bool HeldBeforeCommit { get; set; }
        internal bool HeldAfterCommit { get; set; }
        internal bool OriginalOwnerPreserved { get; private set; } = true;

        internal void Bind(ExploreDbContext context, Guid selectedTenant)
        {
            original = context;
            connector = context.Database.GetDbConnection();
            contextId = context.ContextId;
            tenantId = selectedTenant;
        }

        internal bool IsOriginal(DbContext? context) => ReferenceEquals(context, original) && original is not null;

        internal bool ObserveOwner(DbContext? context, DbConnection? connection)
        {
            if (!IsOriginal(context))
                return false;
            original!.RequireBoundTenantLease();
            OriginalOwnerPreserved &= original!.ContextId == contextId
                && original.TenantFilterTenantId == tenantId
                && ReferenceEquals(original.Database.GetDbConnection(), connector)
                && ReferenceEquals(connection, connector);
            return EventDiscoveryEnrollmentGate.TryRequireCurrent(original);
        }

        internal void ObserveCommand(DbContext? context)
        {
            if (IsOriginal(context))
                Interlocked.Increment(ref commands);
        }
    }

    private sealed class LocalWriterTransactionObserver(LocalWriterObservation observation) : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (observation.IsOriginal(eventData.Context))
            {
                observation.HeldBeforeBegin = observation.ObserveOwner(eventData.Context, connection);
                observation.Begin.TrySetResult();
            }
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (observation.IsOriginal(eventData.Context))
            {
                observation.HeldBeforeCommit = observation.ObserveOwner(eventData.Context, transaction.Connection);
                observation.Committing.TrySetResult();
                await observation.AllowCommit.Task.WaitAsync(cancellationToken);
            }
            return result;
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (observation.IsOriginal(eventData.Context))
                observation.HeldAfterCommit = observation.ObserveOwner(
                    eventData.Context, eventData.Context!.Database.GetDbConnection());
            return Task.CompletedTask;
        }
    }

    private sealed class LocalWriterCommandObserver(LocalWriterObservation observation) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            observation.ObserveCommand(eventData.Context);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            observation.ObserveCommand(eventData.Context);
            return ValueTask.FromResult(result);
        }
    }
}
