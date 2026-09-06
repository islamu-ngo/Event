// ABOUTME: Verifies provider commands, failure results, transaction guards, and real SQLite named-lock leases.
// ABOUTME: Covers the provider-neutral lock boundary without claiming unexecuted server-engine behavior.

using System.Data.Common;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MySqlConnector;
using Npgsql;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Database;

public sealed class RelationalNamedLockTests
{
    [Test]
    public async Task ProviderCommands_UseRequiredOwnershipAndBoundedMySqlName()
    {
        const string resource = "notification-fanout-precedence:tenant:event";
        using DbCommand postgresTransaction = RelationalNamedLock.CreateAcquireCommand(
            new NpgsqlConnection(), null, RelationalNamedLock.PostgreSqlProvider, resource, true);
        using DbCommand postgresSession = RelationalNamedLock.CreateAcquireCommand(
            new NpgsqlConnection(), null, RelationalNamedLock.PostgreSqlProvider, resource, false);
        using DbCommand sqlServerTransaction = RelationalNamedLock.CreateAcquireCommand(
            new SqlConnection(), null, RelationalNamedLock.SqlServerProvider, resource, true);
        using DbCommand sqlServerSession = RelationalNamedLock.CreateAcquireCommand(
            new SqlConnection(), null, RelationalNamedLock.SqlServerProvider, resource, false);
        string mySqlResource = RelationalNamedLock.NormalizeProviderResource(
            RelationalNamedLock.MySqlProvider,
            new string('x', 500));
        using DbCommand mySql = RelationalNamedLock.CreateAcquireCommand(
            new MySqlConnection(), null, RelationalNamedLock.MySqlProvider, mySqlResource, true);
        using DbCommand postgresRelease = RelationalNamedLock.CreateReleaseCommand(
            new NpgsqlConnection(), RelationalNamedLock.PostgreSqlProvider, resource);
        using DbCommand sqlServerRelease = RelationalNamedLock.CreateReleaseCommand(
            new SqlConnection(), RelationalNamedLock.SqlServerProvider, resource);
        using DbCommand mySqlRelease = RelationalNamedLock.CreateReleaseCommand(
            new MySqlConnection(), RelationalNamedLock.MySqlProvider, mySqlResource);

        await Assert.That(postgresTransaction.CommandText).IsEqualTo("SELECT pg_advisory_xact_lock(@key)");
        await Assert.That(postgresSession.CommandText).IsEqualTo("SELECT pg_advisory_lock(@key)");
        await Assert.That(sqlServerTransaction.CommandText).Contains("@LockOwner = 'Transaction'");
        await Assert.That(sqlServerSession.CommandText).Contains("@LockOwner = 'Session'");
        await Assert.That(mySql.CommandText).IsEqualTo("SELECT GET_LOCK(@resource, 31536000)");
        await Assert.That(postgresRelease.CommandText).IsEqualTo("SELECT pg_advisory_unlock(@key)");
        await Assert.That(sqlServerRelease.CommandText).Contains("sys.sp_releaseapplock");
        await Assert.That(mySqlRelease.CommandText).IsEqualTo("SELECT RELEASE_LOCK(@resource)");
        await Assert.That(mySqlResource).Length().IsEqualTo(64);
        await Assert.That(mySqlResource).StartsWith("explore:");
        await Assert.That(mySql.Parameters[0].Value).IsEqualTo(mySqlResource);
    }

    [Test]
    public async Task ProviderResults_FailClosedOnAcquireAndReleaseFailure()
    {
        RelationalNamedLock.EnsureAcquireSucceeded(RelationalNamedLock.SqlServerProvider, 0);
        RelationalNamedLock.EnsureAcquireSucceeded(RelationalNamedLock.MySqlProvider, 1L);
        RelationalNamedLock.EnsureReleaseSucceeded(RelationalNamedLock.PostgreSqlProvider, true);
        RelationalNamedLock.EnsureReleaseSucceeded(RelationalNamedLock.SqlServerProvider, 0);
        RelationalNamedLock.EnsureReleaseSucceeded(RelationalNamedLock.MySqlProvider, 1L);

        Assert.Throws<InvalidOperationException>(() =>
            RelationalNamedLock.EnsureAcquireSucceeded(RelationalNamedLock.SqlServerProvider, -1));
        Assert.Throws<InvalidOperationException>(() =>
            RelationalNamedLock.EnsureAcquireSucceeded(RelationalNamedLock.MySqlProvider, 0));
        Assert.Throws<InvalidOperationException>(() =>
            RelationalNamedLock.EnsureReleaseSucceeded(RelationalNamedLock.PostgreSqlProvider, false));
        Assert.Throws<InvalidOperationException>(() =>
            RelationalNamedLock.EnsureReleaseSucceeded(RelationalNamedLock.MySqlProvider, null));

        await Task.CompletedTask;
    }

    [Test]
    public async Task MySqlReleaseFailure_ClosesConnectionBeforeItCanReturnToPool()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using DbTransaction transaction = await connection.BeginTransactionAsync();
        var interceptor = new MySqlNamedLockTransactionInterceptor();
        interceptor.Track(transaction, "explore:test-lock");

        Assert.Throws<SqliteException>(() => interceptor.ReleaseTracked(transaction));

        await Assert.That(connection.State).IsEqualTo(System.Data.ConnectionState.Closed);
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql)]
    [Arguments(PrimaryDatabaseProvider.Sqlite)]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    public async Task TransactionLock_RequiresActiveTransactionForEveryRelationalProvider(
        PrimaryDatabaseProvider provider)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, CreateOptions(provider));
        await using var context = new ExploreDbContext(options.Options);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RelationalNamedLock.AcquireTransactionAsync(context, "test-lock", CancellationToken.None));
    }

    [Test]
    public async Task SqliteSessionLease_SerializesAcrossContextsAndReleases()
    {
        await using ExploreDbContext firstContext = CreateSqliteContext();
        await using ExploreDbContext secondContext = CreateSqliteContext();
        var firstLock = new RelationalAtprotoSessionRefreshLock(firstContext);
        var secondLock = new RelationalAtprotoSessionRefreshLock(secondContext);
        Guid tenantId = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();

        IAsyncDisposable firstLease = await firstLock.AcquireAsync(
            tenantId, userId, "pds", "did:plc:sqlite-lock", CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        Task<IAsyncDisposable> waiting = secondLock.AcquireAsync(
            tenantId, userId, "pds", "did:plc:sqlite-lock", cancellation.Token);

        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await using IAsyncDisposable lease = await waiting;
        });
        await firstLease.DisposeAsync();
        await using IAsyncDisposable secondLease = await secondLock.AcquireAsync(
            tenantId,
            userId,
            "pds",
            "did:plc:sqlite-lock",
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task SqliteTransactionLock_ImplicitRollbackAllowsNextTransactionOnSameConnection()
    {
        await using ExploreDbContext context = CreateSqliteContext();
        await context.Database.OpenConnectionAsync();
        string resource = $"same-connection-rollback:{Guid.CreateVersion7():N}";
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await using IAsyncDisposable lease = await RelationalNamedLock.AcquireTransactionAsync(
                context, resource, CancellationToken.None);
        }

        await using var nextTransaction = await context.Database.BeginTransactionAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using IAsyncDisposable nextLease = await RelationalNamedLock.AcquireTransactionAsync(
            context, resource, cancellation.Token);
        await nextTransaction.RollbackAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SqliteTransactionLock_ImplicitRollbackReleasesOnConnectionCleanup(bool disposeContext)
    {
        await using ExploreDbContext firstContext = CreateSqliteContext();
        await using ExploreDbContext secondContext = CreateSqliteContext();
        string resource = $"implicit-rollback:{Guid.CreateVersion7():N}";
        await using var firstTransaction = await firstContext.Database.BeginTransactionAsync();
        await using IAsyncDisposable firstLease = await RelationalNamedLock.AcquireTransactionAsync(
            firstContext, resource, CancellationToken.None);
        await firstLease.DisposeAsync();
        await using var secondTransaction = await secondContext.Database.BeginTransactionAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        Task<IAsyncDisposable> waiting = RelationalNamedLock.AcquireTransactionAsync(
            secondContext, resource, cancellation.Token);
        await Assert.That(waiting.IsCompleted).IsFalse();

        await firstTransaction.DisposeAsync();
        if (disposeContext)
        {
            await firstContext.DisposeAsync();
        }
        else
        {
            await firstContext.Database.CloseConnectionAsync();
        }

        await using IAsyncDisposable nextLease = await waiting;
        await secondTransaction.RollbackAsync();
    }

    [Test]
    public async Task SqliteManifestTransactionLock_HoldsProcessLeaseUntilCallerCommit()
    {
        await using ExploreDbContext firstContext = CreateSqliteContext();
        await using ExploreDbContext secondContext = CreateSqliteContext();
        string resource =
            $"explore:setting-mutation:{ConfigurationManifestLockKeys.InstanceManifest}.{Guid.CreateVersion7():N}";
        await using var firstTransaction =
            await firstContext.Database.BeginTransactionAsync();
        await using IAsyncDisposable firstLease =
            await RelationalNamedLock.AcquireTransactionAsync(
                firstContext,
                resource,
                CancellationToken.None);
        await using var secondTransaction =
            await secondContext.Database.BeginTransactionAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            RelationalNamedLock.AcquireTransactionAsync(
                secondContext,
                resource,
                cancellation.Token));

        await firstTransaction.CommitAsync();
        await using IAsyncDisposable secondLease =
            await RelationalNamedLock.AcquireTransactionAsync(
                    secondContext,
                    resource,
                    CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(2));
        await secondTransaction.RollbackAsync();
    }

    [Test]
    public async Task OrderedOuterLease_SkipsOverlappingInnerLockAndClearsScope()
    {
        await using ExploreDbContext firstContext = CreateSqliteContext();
        await using ExploreDbContext blockerContext = CreateSqliteContext();
        var unitOfWork = new EfCoreUnitOfWork(firstContext);
        var mutationLock = new RelationalSettingMutationLock(
            firstContext,
            unitOfWork);
        string key =
            $"{ConfigurationManifestLockKeys.InstanceManifest}.{Guid.CreateVersion7():N}";

        bool nestedEntered = await mutationLock.ExecuteOrderedGroupsAsync(
                new[] { new[] { key } },
                token => unitOfWork.ExecuteSerializableAsync(
                    async _ =>
                    {
                        using var cancellation =
                            new CancellationTokenSource();
                        cancellation.Cancel();
                        return await mutationLock.ExecuteManyAsync(
                            [key],
                            _ => Task.FromResult(true),
                            cancellation.Token);
                    },
                    token))
            .WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.That(nestedEntered).IsTrue();

        using var outerCancellation = new CancellationTokenSource();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            mutationLock.ExecuteOrderedGroupsAsync(
                new[] { new[] { key } },
                token => unitOfWork.ExecuteSerializableAsync<bool>(
                    _ =>
                    {
                        outerCancellation.Cancel();
                        return Task.FromCanceled<bool>(
                            outerCancellation.Token);
                    },
                    token)));

        string resource = $"explore:setting-mutation:{key}";
        await using var blockerTransaction =
            await blockerContext.Database.BeginTransactionAsync();
        await using IAsyncDisposable blockerLease =
            await RelationalNamedLock.AcquireTransactionAsync(
                    blockerContext,
                    resource,
                    CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(2));
        await using var probeTransaction =
            await firstContext.Database.BeginTransactionAsync();
        using var probeCancellation = new CancellationTokenSource();
        probeCancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            mutationLock.ExecuteManyAsync(
                [key],
                _ => Task.FromResult(true),
                probeCancellation.Token));

        await probeTransaction.RollbackAsync();
        await blockerTransaction.RollbackAsync();
    }

    [Test]
    public async Task SqliteSettingLock_ReleasesProcessLeaseAfterCancellationRollback()
    {
        await using ExploreDbContext firstContext = CreateSqliteContext();
        await using ExploreDbContext secondContext = CreateSqliteContext();
        var firstLock = new RelationalSettingMutationLock(
            firstContext,
            new EfCoreUnitOfWork(firstContext));
        var secondLock = new RelationalSettingMutationLock(
            secondContext,
            new EfCoreUnitOfWork(secondContext));
        using var cancellation = new CancellationTokenSource();

        string key = $"governance.lock.{Guid.CreateVersion7():N}";
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            firstLock.ExecuteAsync<bool>(
                key,
                _ =>
                {
                    cancellation.Cancel();
                    return Task.FromCanceled<bool>(cancellation.Token);
                },
                cancellation.Token));

        bool entered = await secondLock.ExecuteAsync(
                key,
                _ => Task.FromResult(true))
            .WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.That(entered).IsTrue();
    }

    private static ExploreDbContext CreateSqliteContext()
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        builder.UseSqlite("Data Source=:memory:");
        builder.AddInterceptors(
            SqliteNamedLockTransactionInterceptor.Instance,
            SqliteProjectionLockTransactionInterceptor.Instance);
        return new ExploreDbContext(builder.Options);
    }

    private static PrimaryDatabaseConnectionOptions CreateOptions(PrimaryDatabaseProvider provider) => new()
    {
        Role = PrimaryDatabaseRole.Runtime,
        Provider = provider,
        Host = provider == PrimaryDatabaseProvider.Sqlite ? null : "localhost",
        Port = provider switch
        {
            PrimaryDatabaseProvider.PostgreSql => 5432,
            PrimaryDatabaseProvider.SqlServer => 1433,
            PrimaryDatabaseProvider.MariaDb or PrimaryDatabaseProvider.MySql => 3306,
            _ => null,
        },
        Database = provider == PrimaryDatabaseProvider.Sqlite ? "lock-tests.db" : "event",
        Username = provider == PrimaryDatabaseProvider.Sqlite ? null : "event",
        Password = provider == PrimaryDatabaseProvider.Sqlite
            ? null
            : Guid.CreateVersion7().ToString("N"),
        TlsMode = provider == PrimaryDatabaseProvider.Sqlite
            ? PrimaryDatabaseTlsMode.Prefer
            : PrimaryDatabaseTlsMode.Disabled,
        ServerFlavor = provider switch
        {
            PrimaryDatabaseProvider.MariaDb => PrimaryDatabaseServerFlavor.MariaDb,
            PrimaryDatabaseProvider.MySql => PrimaryDatabaseServerFlavor.MySql,
            _ => null,
        },
        ServerVersion = provider switch
        {
            PrimaryDatabaseProvider.MariaDb => new Version(11, 4),
            PrimaryDatabaseProvider.MySql => new Version(8, 4),
            _ => null,
        },
    };
}
