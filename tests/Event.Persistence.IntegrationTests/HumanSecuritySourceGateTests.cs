using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests;

/// <summary>Original-owner cooperative gate custody; SQLite cross-process exclusion is not claimed.</summary>
[NotInParallel]
public sealed class HumanSecuritySourceGateTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CompetingSourceOwnerCannotEnterUntilTheFirstOwnerSettlesAndReleases(bool rollback)
    {
        await using var original = CreateContext();
        await using var competing = CreateContext();
        await using var gate = new HumanSecuritySourceGate(original);
        var held = await gate.AcquireAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task<IAsyncDisposable>? waiting = null;
        try
        {
            await using (var transaction = await original.Database.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                original.ChangeTracker.Clear();
                gate.RequireCurrent();
                await using var secondGate = new HumanSecuritySourceGate(competing);
                waiting = secondGate.AcquireAsync(cancellation.Token);
                await Assert.That(waiting.IsCompleted).IsFalse();
                if (rollback)
                    await transaction.RollbackAsync();
                else
                    await transaction.CommitAsync();
                gate.RequireCurrent();
                await Assert.That(waiting.IsCompleted).IsFalse();
                await held.DisposeAsync();
                await using var next = await waiting;
                secondGate.RequireCurrent();
                await Assert.That(competing.Database.CurrentTransaction).IsNull();
            }
        }
        finally
        {
            cancellation.Cancel();
            await held.DisposeAsync();
            if (waiting is not null)
            {
                try { await using var cleanup = await waiting; }
                catch (OperationCanceledException) { }
            }
        }
    }

    [Test]
    public async Task ExistingSnapshotCannotBeRetrofittedWithSecuritySourceGate()
    {
        await using var context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var connector = context.Database.GetDbConnection();
        await using (var command = connector.CreateCommand())
        {
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync();
        }
        await using var gate = new HumanSecuritySourceGate(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.AcquireAsync());

        await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
        await Assert.That(context.Database.GetDbConnection()).IsSameReferenceAs(connector);
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task SecuritySourceGatePrecedesButDoesNotAliasDiscoveryEnrollmentResource()
    {
        await using var context = CreateContext();
        await using var security = new HumanSecuritySourceGate(context);
        await using var securityLease = await security.AcquireAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var discovery = new EventDiscoveryEnrollmentGate(context);
        await using var discoveryLease = await discovery.AcquireAsync(cancellation.Token);

        security.RequireCurrent();
        discovery.RequireCurrent();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        HumanSecuritySourceGate.RequireCurrent(context);
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task CanceledWaiterCannotReleaseTheEarlierOwnersSecuritySourceGate()
    {
        await using var original = CreateContext();
        await using var contender = CreateContext();
        await using var first = new HumanSecuritySourceGate(original);
        await using var held = await first.AcquireAsync();
        await using var second = new HumanSecuritySourceGate(contender);
        using var cancellation = new CancellationTokenSource();
        var waiting = second.AcquireAsync(cancellation.Token);
        await Assert.That(waiting.IsCompleted).IsFalse();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => { await using var unexpected = await waiting; });
        first.RequireCurrent();
        await Assert.That(contender.Database.GetDbConnection().State).IsEqualTo(ConnectionState.Closed);

        await held.DisposeAsync();
        await using var next = await second.AcquireAsync().WaitAsync(TimeSpan.FromSeconds(2));
        second.RequireCurrent();
    }

    [Test]
    public async Task BorrowProbeDistinguishesAbsentAndHeldCustodyButRefusesAnExistingPendingOwner()
    {
        await using var original = CreateContext();
        await using var contender = CreateContext();
        await Assert.That(HumanSecuritySourceGate.TryRequireCurrent(original)).IsFalse();
        await using var first = new HumanSecuritySourceGate(original);
        await using var held = await first.AcquireAsync();
        await Assert.That(HumanSecuritySourceGate.TryRequireCurrent(original)).IsTrue();
        await using var second = new HumanSecuritySourceGate(contender);
        using var cancellation = new CancellationTokenSource();
        var waiting = second.AcquireAsync(cancellation.Token);
        try
        {
            await Assert.That(waiting.IsCompleted).IsFalse();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
            {
                HumanSecuritySourceGate.TryRequireCurrent(contender);
                return Task.CompletedTask;
            });
        }
        finally
        {
            cancellation.Cancel();
            try { await using var unexpected = await waiting; }
            catch (OperationCanceledException) { }
        }
        await Assert.That(HumanSecuritySourceGate.TryRequireCurrent(contender)).IsFalse();
        await Assert.That(HumanSecuritySourceGate.TryRequireCurrent(original)).IsTrue();
    }

    private static ExploreDbContext CreateContext()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(SqliteTestDatabaseFactory.CreateIsolatedMemoryConnectionString())
            .AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance).Options;
        var context = new ExploreDbContext(options);
        context.BindTenantLease();
        return context;
    }
}
