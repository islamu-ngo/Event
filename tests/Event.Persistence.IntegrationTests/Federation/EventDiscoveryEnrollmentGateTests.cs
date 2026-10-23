using System.Data;
using System.Transactions;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Services;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Federation;

[NotInParallel]
public sealed class EventDiscoveryEnrollmentGateTests
{
    private const string EnrollmentResource = "discovery-tenant-enrollment";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcquirePinsTheOriginalIdleConnectorAndPreservesConnectionOwnership(bool initiallyOpen)
    {
        await using var context = CreateContext();
        if (initiallyOpen)
            await context.Database.OpenConnectionAsync();
        var connector = context.Database.GetDbConnection();
        context.RequireBoundTenantLease();
        IEventDiscoveryEnrollmentGate gate = new EventDiscoveryEnrollmentGate(context);

        var lease = await gate.AcquireAsync();
        try
        {
            await Assert.That(context.Database.GetDbConnection()).IsSameReferenceAs(connector);
            await Assert.That(connector.State).IsEqualTo(ConnectionState.Open);
            await Assert.That(context.Database.CurrentTransaction).IsNull();
            context.RequireBoundTenantLease();
            await using var command = connector.CreateCommand();
            command.CommandText = "SELECT 1";
            await Assert.That(Convert.ToInt32(await command.ExecuteScalarAsync())).IsEqualTo(1);
        }
        finally
        {
            await lease.DisposeAsync();
        }

        await Assert.That(connector.State).IsEqualTo(initiallyOpen ? ConnectionState.Open : ConnectionState.Closed);
        context.RequireBoundTenantLease();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PhysicalEnrollmentLeaseSurvivesCommitOrRollbackUntilItsOwnDisposal(bool rollback)
    {
        await using var context = CreateContext();
        await using var contender = CreateContext();
        var gate = new EventDiscoveryEnrollmentGate(context);
        var connector = context.Database.GetDbConnection();
        await using var lease = await gate.AcquireAsync();

        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await RequireFixedResourceBlockedAsync(contender);
            if (rollback)
                await transaction.RollbackAsync();
            else
                await transaction.CommitAsync();
        }

        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.Database.GetDbConnection()).IsSameReferenceAs(connector);
        await Assert.That(connector.State).IsEqualTo(ConnectionState.Open);
        context.RequireBoundTenantLease();
        await RequireFixedResourceBlockedAsync(contender);

        await lease.DisposeAsync();
        await using var next = await new EventDiscoveryEnrollmentGate(contender)
            .AcquireAsync().WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task CanceledCompetingGateCleansItsAttemptAndCanAcquireAfterTheHolderReleases()
    {
        await using var holder = CreateContext();
        await using var contender = CreateContext();
        await using var firstLease = await new EventDiscoveryEnrollmentGate(holder).AcquireAsync();
        var waitingGate = new EventDiscoveryEnrollmentGate(contender);
        using var cancellation = new CancellationTokenSource();
        Task<IAsyncDisposable> waiting = waitingGate.AcquireAsync(cancellation.Token);

        try
        {
            await Assert.That(waiting.IsCompleted).IsFalse();
            cancellation.Cancel();
            await Assert.That(async () => { await using var unexpected = await waiting; })
                .Throws<OperationCanceledException>();
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                await using var cleanup = await waiting;
            }
            catch (OperationCanceledException)
            {
            }
        }
        await Assert.That(contender.Database.GetDbConnection().State).IsEqualTo(ConnectionState.Closed);
        contender.RequireBoundTenantLease();
        await RequireFixedResourceBlockedAsync(contender);

        await firstLease.DisposeAsync();
        await using var next = await waitingGate.AcquireAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.That(contender.Database.GetDbConnection().State).IsEqualTo(ConnectionState.Open);
    }

    [Test]
    public async Task AbandoningTheGateOwnerReleasesThePhysicalResourceWithoutWaitingForTheReturnedLease()
    {
        await using var context = CreateContext();
        await using var contender = CreateContext();
        var gate = new EventDiscoveryEnrollmentGate(context);
        await using var abandonedLease = await gate.AcquireAsync();
        await RequireFixedResourceBlockedAsync(contender);

        await gate.DisposeAsync();

        await Assert.That(context.Database.GetDbConnection().State).IsEqualTo(ConnectionState.Closed);
        await using var next = await new EventDiscoveryEnrollmentGate(contender)
            .AcquireAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await abandonedLease.DisposeAsync();
        await RequireFixedResourceBlockedAsync(context);
        await Assert.That(() => gate.AcquireAsync()).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task ReentryOnTheSameOriginalContextFailsImmediatelyInsteadOfWaitingOnItsOwnGate()
    {
        await using var context = CreateContext();
        var gate = new EventDiscoveryEnrollmentGate(context);
        await using var lease = await gate.AcquireAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await Assert.That(() => gate.AcquireAsync(cancellation.Token).WaitAsync(TimeSpan.FromSeconds(2)))
            .Throws<InvalidOperationException>();
        var secondIssuer = new EventDiscoveryEnrollmentGate(context);
        await Assert.That(() => secondIssuer.AcquireAsync(cancellation.Token).WaitAsync(TimeSpan.FromSeconds(2)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AnAlreadyCanceledAcquireDoesNotRetainAConnectorOrEnrollmentResource()
    {
        await using var context = CreateContext();
        var gate = new EventDiscoveryEnrollmentGate(context);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.That(() => gate.AcquireAsync(cancellation.Token)).Throws<OperationCanceledException>();
        await Assert.That(context.Database.GetDbConnection().State).IsEqualTo(ConnectionState.Closed);

        await using var next = await gate.AcquireAsync().WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task ExistingTransactionCannotBeRetrofittedWithAPreBeginEnrollmentGate()
    {
        await using var context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var gate = new EventDiscoveryEnrollmentGate(context);

        await Assert.That(() => gate.AcquireAsync()).Throws<InvalidOperationException>();
        await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task AmbientTransactionCannotSupplyThePreBeginOwner()
    {
        await using var context = CreateContext();
        var connector = context.Database.GetDbConnection();
        var gate = new EventDiscoveryEnrollmentGate(context);
        using var ambient = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

        await Assert.That(() => gate.AcquireAsync()).Throws<InvalidOperationException>();
        await Assert.That(connector.State).IsEqualTo(ConnectionState.Closed);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task AcquisitionCannotMintAnUnsealedContextLease()
    {
        await using var context = CreateContext(bind: false);
        var gate = new EventDiscoveryEnrollmentGate(context);

        await Assert.That(() => gate.AcquireAsync()).Throws<InvalidOperationException>();
        await Assert.That(context.Database.GetDbConnection().State).IsEqualTo(ConnectionState.Closed);
        await Assert.That(() => context.RequireBoundTenantLease()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AChangedSelectedTenantIsRejectedBeforeTheGateOpensItsConnector()
    {
        var tenant = new SelectedTenant(Guid.CreateVersion7());
        await using var context = CreateContext(tenant: tenant);
        tenant.TenantId = Guid.CreateVersion7();

        await Assert.That(() => new EventDiscoveryEnrollmentGate(context).AcquireAsync())
            .Throws<InvalidOperationException>();
        await Assert.That(context.Database.GetDbConnection().State).IsEqualTo(ConnectionState.Closed);
    }

    [Test]
    public async Task ReleasingALeaseAllowsANewAttemptOnTheSameSealedContext()
    {
        var tenant = new SelectedTenant(Guid.CreateVersion7());
        await using var context = CreateContext(tenant: tenant);
        await using var contender = CreateContext();
        var gate = new EventDiscoveryEnrollmentGate(context);
        var firstLease = await gate.AcquireAsync();
        await firstLease.DisposeAsync();

        await using var next = await gate.AcquireAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(tenant.TenantId);
        context.RequireBoundTenantLease();
        await RequireFixedResourceBlockedAsync(contender);
    }

    [Test]
    public async Task StalePooledGateCannotCloseOrAbortTheNewRentersConnectorAndTransaction()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(SqliteTestDatabaseFactory.CreateIsolatedMemoryConnectionString())
            .AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance)
            .Options;
        var factory = new PooledDbContextFactory<ExploreDbContext>(options, poolSize: 1);
        var original = await factory.CreateDbContextAsync();
        original.BindTenantLease();
        var originalId = original.ContextId;
        var oldGate = new EventDiscoveryEnrollmentGate(original);
        var oldLease = await oldGate.AcquireAsync();
        bool returned = false;
        try
        {
            await original.DisposeAsync();
            returned = true;
            await using var renter = await factory.CreateDbContextAsync();
            renter.BindTenantLease();
            await Assert.That(renter).IsSameReferenceAs(original);
            await Assert.That(renter.ContextId).IsNotEqualTo(originalId);
            await renter.Database.OpenConnectionAsync();
            var connector = renter.Database.GetDbConnection();
            InvalidOperationException? custodyLoss = null;
            await using (var transaction = await renter.Database.BeginTransactionAsync())
            {
                await Assert.That(() => EventDiscoveryEnrollmentGate.TryRequireCurrent(renter))
                    .Throws<InvalidOperationException>();
                try
                {
                    await oldGate.DisposeAsync();
                }
                catch (InvalidOperationException failure)
                {
                    custodyLoss = failure;
                }

                await Assert.That(renter.Database.CurrentTransaction).IsSameReferenceAs(transaction);
                await Assert.That(connector.State).IsEqualTo(ConnectionState.Open);
                await using var command = connector.CreateCommand();
                command.Transaction = transaction.GetDbTransaction();
                command.CommandText = "SELECT 1";
                await Assert.That(Convert.ToInt32(await command.ExecuteScalarAsync())).IsEqualTo(1);
                await transaction.CommitAsync();
            }

            await Assert.That(connector.State).IsEqualTo(ConnectionState.Open);
            renter.RequireBoundTenantLease();
            await using var probe = CreateContext();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await using var resource = await RelationalNamedLock.AcquireSessionAsync(
                probe, EnrollmentResource, cancellation.Token);
            await Assert.That(custodyLoss).IsNotNull();
            await renter.Database.CloseConnectionAsync();
        }
        finally
        {
            try
            {
                await oldLease.DisposeAsync();
            }
            catch (InvalidOperationException)
            {
                // Lost custody is the expected cleanup result; it must never touch a later renter.
            }
            if (!returned)
                await original.DisposeAsync();
        }
    }

    [Test]
    public async Task AbandoningABlockedServiceCancelsItsAcquisitionAndReleasesOnlyItsOwnConnector()
    {
        await using var holder = CreateContext();
        await using var contender = CreateContext();
        await using var probe = CreateContext();
        await using var holderLease = await new EventDiscoveryEnrollmentGate(holder).AcquireAsync();
        var abandoned = new EventDiscoveryEnrollmentGate(contender);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var waiting = abandoned.AcquireAsync(cancellation.Token);
        try
        {
            await Assert.That(waiting.IsCompleted).IsFalse();
            await abandoned.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            await Assert.That(async () => { await using var unexpected = await waiting; })
                .Throws<OperationCanceledException>();
            await Assert.That(contender.Database.GetDbConnection().State).IsEqualTo(ConnectionState.Closed);
            contender.RequireBoundTenantLease();
            await Assert.That(holder.Database.GetDbConnection().State).IsEqualTo(ConnectionState.Open);
            await RequireFixedResourceBlockedAsync(probe);
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                await using var cleanup = await waiting;
            }
            catch (OperationCanceledException)
            {
            }
        }
        await holderLease.DisposeAsync();
        using var released = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var next = await RelationalNamedLock.AcquireSessionAsync(probe, EnrollmentResource, released.Token);
    }

    [Test]
    public async Task CurrentCustodyProofExpiresWhenItsLeaseIsReleased()
    {
        await using var context = CreateContext();
        var gate = new EventDiscoveryEnrollmentGate(context);
        var lease = await gate.AcquireAsync();
        EventDiscoveryEnrollmentGate.RequireCurrent(context);
        await lease.DisposeAsync();

        await Assert.That(() => EventDiscoveryEnrollmentGate.RequireCurrent(context))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ReopeningTheConnectorDoesNotRestoreItsExpiredCustodyProofOrTransferCleanupOwnership()
    {
        await using var context = CreateContext();
        var gate = new EventDiscoveryEnrollmentGate(context);
        var lease = await gate.AcquireAsync();
        try
        {
            EventDiscoveryEnrollmentGate.RequireCurrent(context);
            await context.Database.CloseConnectionAsync();
            await context.Database.OpenConnectionAsync();

            await Assert.That(() => EventDiscoveryEnrollmentGate.RequireCurrent(context))
                .Throws<InvalidOperationException>();
            await Assert.That(() => lease.DisposeAsync().AsTask()).Throws<InvalidOperationException>();
            await Assert.That(context.Database.GetDbConnection().State).IsEqualTo(ConnectionState.Open);
            await using var probe = CreateContext();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await using var released = await RelationalNamedLock.AcquireSessionAsync(
                probe, EnrollmentResource, cancellation.Token);
        }
        finally
        {
            try
            {
                await lease.DisposeAsync();
            }
            catch (InvalidOperationException)
            {
                // A stale lease remains invalid after its one physical cleanup attempt.
            }
        }
    }

    [Test]
    public async Task OptionalCustodyProbeDistinguishesAbsenceFromTheSameOwnersLiveLease()
    {
        await using var context = CreateContext(bind: false);
        await Assert.That(EventDiscoveryEnrollmentGate.TryRequireCurrent(context)).IsFalse();
        await Assert.That(() => context.RequireBoundTenantLease()).Throws<InvalidOperationException>();
        context.BindTenantLease();
        var lease = await new EventDiscoveryEnrollmentGate(context).AcquireAsync();
        try
        {
            await Assert.That(EventDiscoveryEnrollmentGate.TryRequireCurrent(context)).IsTrue();
            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                await Assert.That(EventDiscoveryEnrollmentGate.TryRequireCurrent(context)).IsTrue();
                await transaction.CommitAsync();
            }
            await Assert.That(EventDiscoveryEnrollmentGate.TryRequireCurrent(context)).IsTrue();
        }
        finally
        {
            await lease.DisposeAsync();
        }
        await Assert.That(EventDiscoveryEnrollmentGate.TryRequireCurrent(context)).IsFalse();
        context.RequireBoundTenantLease();
    }

    [Test]
    public async Task OptionalCustodyProbeRejectsPendingAcquisitionInsteadOfReportingAbsence()
    {
        await using var holder = CreateContext();
        await using var contender = CreateContext();
        await using var holderLease = await new EventDiscoveryEnrollmentGate(holder).AcquireAsync();
        using var cancellation = new CancellationTokenSource();
        var waiting = new EventDiscoveryEnrollmentGate(contender).AcquireAsync(cancellation.Token);
        try
        {
            await Assert.That(waiting.IsCompleted).IsFalse();
            await Assert.That(() => EventDiscoveryEnrollmentGate.TryRequireCurrent(contender))
                .Throws<InvalidOperationException>();
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                await using var unexpected = await waiting;
            }
            catch (OperationCanceledException)
            {
            }
        }
        await Assert.That(EventDiscoveryEnrollmentGate.TryRequireCurrent(contender)).IsFalse();
    }

    [Test]
    public async Task OptionalCustodyProbeRejectsAChangedTenantWithoutSilentlyRebinding()
    {
        var tenant = new SelectedTenant(Guid.CreateVersion7());
        var originalTenantId = tenant.TenantId;
        await using var context = CreateContext(tenant: tenant);
        var lease = await new EventDiscoveryEnrollmentGate(context).AcquireAsync();
        try
        {
            tenant.TenantId = Guid.CreateVersion7();
            await Assert.That(() => EventDiscoveryEnrollmentGate.TryRequireCurrent(context))
                .Throws<InvalidOperationException>();
            await Assert.That(() => context.RequireBoundTenantLease()).Throws<InvalidOperationException>();
        }
        finally
        {
            tenant.TenantId = originalTenantId;
            await lease.DisposeAsync();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OptionalCustodyProbeRejectsConnectorLossInsteadOfReportingAbsence(bool reopen)
    {
        await using var context = CreateContext();
        var lease = await new EventDiscoveryEnrollmentGate(context).AcquireAsync();
        try
        {
            await context.Database.CloseConnectionAsync();
            if (reopen)
                await context.Database.OpenConnectionAsync();
            await Assert.That(() => EventDiscoveryEnrollmentGate.TryRequireCurrent(context))
                .Throws<InvalidOperationException>();
        }
        finally
        {
            try
            {
                await lease.DisposeAsync();
            }
            catch (InvalidOperationException)
            {
                // Invalid custody is still cleaned up; it cannot be reclassified as absent before cleanup.
            }
        }
        await Assert.That(EventDiscoveryEnrollmentGate.TryRequireCurrent(context)).IsFalse();
    }

    [Test]
    public async Task CompletedLeaseCleanupFailureDoesNotReplaceTheComposedOutcomeDuringScopedDisposal()
    {
        await using var original = CreateContext();
        await using var contender = CreateContext();
        var services = new ServiceCollection();
        services.AddScoped<IEventDiscoveryEnrollmentGate>(_ => new EventDiscoveryEnrollmentGate(original));
        await using var provider = services.BuildIsolatedServiceProvider(validateScopes: true);
        var scope = provider.CreateAsyncScope();
        var gate = scope.ServiceProvider.GetRequiredService<IEventDiscoveryEnrollmentGate>();
        var lease = await gate.AcquireAsync();
        await original.Database.GetDbConnection().CloseAsync();

        var cleanup = await Assert.ThrowsAsync<InvalidOperationException>(() => lease.DisposeAsync().AsTask());
        await Assert.That(cleanup.Message).IsEqualTo("discovery_enrollment_cleanup_lost_custody");
        await Assert.That(EventDiscoveryEnrollmentGate.TryRequireCurrent(original)).IsFalse();
        await Assert.That(original.Database.CurrentTransaction).IsNull();
        await using var nextGate = new EventDiscoveryEnrollmentGate(contender);
        await using var next = await nextGate.AcquireAsync().WaitAsync(TimeSpan.FromSeconds(2));
        nextGate.RequireCurrent();

        // The lease still owns its first cleanup failure; DI only owns the adapter.
        var repeated = await Assert.ThrowsAsync<InvalidOperationException>(() => lease.DisposeAsync().AsTask());
        await Assert.That(repeated).IsSameReferenceAs(cleanup);
        var primary = new InvalidOperationException("Original operation failure retained by its caller.");
        var combined = new AggregateException(primary, cleanup);
        var observed = await Assert.ThrowsAsync<AggregateException>(async () =>
        {
            await using (scope)
                throw combined;
        });
        await Assert.That(observed).IsSameReferenceAs(combined);
        await Assert.That(observed.InnerExceptions[0]).IsSameReferenceAs(primary);
        await Assert.That(observed.InnerExceptions[1]).IsSameReferenceAs(cleanup);
        nextGate.RequireCurrent();
    }

    private static async Task RequireFixedResourceBlockedAsync(ExploreDbContext contender)
    {
        using var cancellation = new CancellationTokenSource();
        Task<IAsyncDisposable> waiting = RelationalNamedLock.AcquireSessionAsync(
            contender, EnrollmentResource, cancellation.Token);
        bool blocked = !waiting.IsCompleted;
        cancellation.Cancel();
        if (!blocked)
        {
            await using var unexpected = await waiting;
            await Assert.That(blocked).IsTrue();
        }
        await Assert.That(async () => { await using var unexpected = await waiting; })
            .Throws<OperationCanceledException>();
    }

    private static ExploreDbContext CreateContext(bool bind = true, ITenantContext? tenant = null)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(SqliteTestDatabaseFactory.CreateIsolatedMemoryConnectionString())
            .AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance)
            .Options;
        var context = new ExploreDbContext(options) { TenantContext = tenant };
        if (bind)
            context.BindTenantLease();
        return context;
    }

    private sealed class SelectedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; set; } = tenantId;
    }
}

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class EventDiscoveryEnrollmentGateNativeTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task ReadinessRejectedInitialOpenClosesTheOriginalConnectorAndAllowsANewGate()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        await using var migrator = new ExploreDbContext(options.Options);
        var entity = migrator.Model.FindEntityType(typeof(AtprotoFederationTenantSetting))!;
        string table = migrator.GetService<ISqlGenerationHelper>()
            .DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
        await using var runtime = fixture.CreateEnrolledTenantDbContext(new SelectedTenant(Guid.CreateVersion7()));
        runtime.BindTenantLease();
        var connector = runtime.Database.GetDbConnection();
        await Assert.That(connector.State).IsEqualTo(ConnectionState.Closed);
        var failedGate = new EventDiscoveryEnrollmentGate(runtime);
        InvalidOperationException? rejection = null;
        ConnectionState stateAtRejection;

        await migrator.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY");
        try
        {
            try
            {
                await using var unexpected = await failedGate.AcquireAsync();
            }
            catch (InvalidOperationException failure)
            {
                rejection = failure;
            }
            stateAtRejection = connector.State;
        }
        finally
        {
            await migrator.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} FORCE ROW LEVEL SECURITY");
        }

        await Assert.That(rejection).IsNotNull();
        await Assert.That(rejection!.Message).IsEqualTo("postgres_tenant_runtime_authority_not_ready");
        await Assert.That(stateAtRejection).IsEqualTo(ConnectionState.Closed);
        runtime.RequireBoundTenantLease();
        await using (var lease = await new EventDiscoveryEnrollmentGate(runtime).AcquireAsync())
        {
            await Assert.That(runtime.Database.GetDbConnection()).IsSameReferenceAs(connector);
            await Assert.That(connector.State).IsEqualTo(ConnectionState.Open);
            await Assert.That(runtime.Database.CurrentTransaction).IsNull();
            EventDiscoveryEnrollmentGate.RequireCurrent(runtime);
        }
        await Assert.That(connector.State).IsEqualTo(ConnectionState.Closed);
        runtime.RequireBoundTenantLease();
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
}
