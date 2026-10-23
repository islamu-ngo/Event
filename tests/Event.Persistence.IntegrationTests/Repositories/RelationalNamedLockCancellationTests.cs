using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Persistence;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class RelationalNamedLockCancellationTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task CancelledLaterWaitPreservesCallerOwnedConnectionAndEarlierLease()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        await using var owner = CreateOwner();
        await using var blocker = CreateOwner();
        await using var observer = CreateOwner();
        string earlierKey = $"named-lock-earlier:{Guid.CreateVersion7():N}";
        string laterKey = $"named-lock-later:{Guid.CreateVersion7():N}";
        await using var blockedLease = await RelationalNamedLock.AcquireSessionAsync(blocker, laterKey, deadline.Token);
        await owner.Database.OpenConnectionAsync(deadline.Token);
        await using var earlier = await RelationalNamedLock.AcquireSessionAsync(owner, earlierKey, deadline.Token);
        var connector = (NpgsqlConnection)owner.Database.GetDbConnection();
        int backend = connector.ProcessID;
        var originalContextId = owner.ContextId;
        var pending = RelationalNamedLock.AcquireSessionAsync(owner, laterKey, cancellation.Token);
        try
        {
            await WaitForOriginalWaiterAsync(observer, backend, laterKey, pending, deadline.Token);
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                var unexpectedLease = await pending;
                await unexpectedLease.DisposeAsync();
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // Join native cleanup before observing cancellation or disposing its owner.
            }
        }
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending);

        await Assert.That(owner.ContextId).IsEqualTo(originalContextId);
        await Assert.That(owner.Database.GetDbConnection()).IsSameReferenceAs(connector);
        await Assert.That(connector.State).IsEqualTo(ConnectionState.Open);
        await Assert.That(connector.ProcessID).IsEqualTo(backend);
        await Assert.That(owner.Database.CurrentTransaction).IsNull();
        await Assert.That(await IsAvailableAsync(observer, earlierKey, deadline.Token)).IsFalse();
        await Assert.That(await IsAvailableAsync(observer, laterKey, deadline.Token)).IsFalse();
        await earlier.DisposeAsync();
        await Assert.That(await IsAvailableAsync(observer, earlierKey, deadline.Token)).IsTrue();
        await Assert.That(connector.State).IsEqualTo(ConnectionState.Open);
        await Assert.That(connector.ProcessID).IsEqualTo(backend);
    }

    [Test]
    public async Task CancelledBeforeSendCannotConsumeAnEarlierSameKeyLease()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var cancelled = new CancellationTokenSource();
        await using var owner = CreateOwner();
        await using var observer = CreateOwner();
        string key = $"named-lock-reentrant-cancel:{Guid.CreateVersion7():N}";
        await owner.Database.OpenConnectionAsync(deadline.Token);
        await using var original = await RelationalNamedLock.AcquireSessionAsync(owner, key, deadline.Token);
        var connection = (NpgsqlConnection)owner.Database.GetDbConnection();
        int backend = connection.ProcessID;
        cancelled.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            RelationalNamedLock.AcquireSessionAsync(owner, key, cancelled.Token));

        await Assert.That(connection.State).IsEqualTo(ConnectionState.Open);
        await Assert.That(connection.ProcessID).IsEqualTo(backend);
        await Assert.That(owner.Database.GetDbConnection()).IsSameReferenceAs(connection);
        await Assert.That(await IsAvailableAsync(observer, key, deadline.Token)).IsFalse();
        await original.DisposeAsync();
        await Assert.That(await IsAvailableAsync(observer, key, deadline.Token)).IsTrue();
        await Assert.That(connection.State).IsEqualTo(ConnectionState.Open);
        await Assert.That(connection.ProcessID).IsEqualTo(backend);
    }

    [Test]
    public async Task ReentrantSuccessStillRequiresEachOriginalLeaseRelease()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var owner = CreateOwner();
        await using var observer = CreateOwner();
        string key = $"named-lock-reentrant:{Guid.CreateVersion7():N}";
        await owner.Database.OpenConnectionAsync(deadline.Token);
        var connection = (NpgsqlConnection)owner.Database.GetDbConnection();
        int backend = connection.ProcessID;
        await using var first = await RelationalNamedLock.AcquireSessionAsync(owner, key, deadline.Token);
        await using var second = await RelationalNamedLock.AcquireSessionAsync(owner, key, deadline.Token);

        await second.DisposeAsync();
        await Assert.That(await IsAvailableAsync(observer, key, deadline.Token)).IsFalse();
        await Assert.That(connection.State).IsEqualTo(ConnectionState.Open);
        await Assert.That(connection.ProcessID).IsEqualTo(backend);
        await first.DisposeAsync();
        await Assert.That(await IsAvailableAsync(observer, key, deadline.Token)).IsTrue();
        await Assert.That(owner.Database.GetDbConnection()).IsSameReferenceAs(connection);
        await Assert.That(connection.State).IsEqualTo(ConnectionState.Open);
        await Assert.That(connection.ProcessID).IsEqualTo(backend);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SentReentrantFailurePreservesEarlierLeasesUntilTheirOwnerSettles(bool grantBeforeFailure)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var owner = CreateOwner();
        await using var observer = CreateOwner();
        await using var migration = fixture.CreateDbContext();
        var connection = (NpgsqlConnection)owner.Database.GetDbConnection();
        // End this exact backend during cleanup, rather than relying on deferred pool reset.
        connection.ConnectionString = new NpgsqlConnectionStringBuilder(connection.ConnectionString)
        {
            Pooling = false
        }.ConnectionString;
        await owner.Database.OpenConnectionAsync(deadline.Token);
        int backend = connection.ProcessID;
        var contextId = owner.ContextId;
        string key = $"named-lock-ambiguous:{Guid.CreateVersion7():N}";
        string unrelatedKey = $"named-lock-unrelated:{Guid.CreateVersion7():N}";
        await using var original = await RelationalNamedLock.AcquireSessionAsync(owner, key, deadline.Token);
        await using var unrelated = await RelationalNamedLock.AcquireSessionAsync(owner, unrelatedKey, deadline.Token);
        string schema = $"named_lock_fault_{Guid.CreateVersion7():N}";
        using var identifiers = new NpgsqlCommandBuilder();
        string quotedSchema = identifiers.QuoteIdentifier(schema);
        string runtime = identifiers.QuoteIdentifier(fixture.NativeQueueEnrollment.RuntimeLogin);
        string originalSearchPath;
        await using (var query = connection.CreateCommand())
        {
            query.CommandText = "SHOW search_path";
            originalSearchPath = (string)(await query.ExecuteScalarAsync(deadline.Token))!;
        }
        await migration.Database.OpenConnectionAsync(deadline.Token);
        await using var setup = migration.Database.GetDbConnection().CreateCommand();
        string acquisition = grantBeforeFailure ? "PERFORM pg_catalog.pg_advisory_lock(p_key);" : string.Empty;
        setup.CommandText = $"""
            CREATE SCHEMA {quotedSchema};
            CREATE FUNCTION {quotedSchema}.pg_advisory_lock(p_key bigint) RETURNS void
                LANGUAGE plpgsql AS $fault$
                BEGIN
                    {acquisition}
                    RAISE EXCEPTION 'named_lock_injected_acquisition_failure' USING ERRCODE = 'P0001';
                END;
                $fault$;
            GRANT USAGE ON SCHEMA {quotedSchema} TO {runtime};
            """;
        try
        {
            await setup.ExecuteNonQueryAsync(deadline.Token);
            await SetSearchPathAsync(connection, quotedSchema + ",pg_catalog", deadline.Token);
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                RelationalNamedLock.AcquireSessionAsync(owner, key, deadline.Token));
            await Assert.That(failure!.Message).IsEqualTo("named_lock_acquisition_cleanup_unconfirmed");
            await Assert.That(failure.InnerException).IsTypeOf<PostgresException>();
            var cause = (PostgresException)failure.InnerException!;
            await Assert.That((cause.SqlState, cause.MessageText))
                .IsEqualTo(("P0001", "named_lock_injected_acquisition_failure"));
            await Assert.That(owner.ContextId).IsEqualTo(contextId);
            await Assert.That(owner.Database.GetDbConnection()).IsSameReferenceAs(connection);
            await Assert.That(connection.State).IsEqualTo(ConnectionState.Open);
            await Assert.That(connection.ProcessID).IsEqualTo(backend);
            await Assert.That(await IsAvailableAsync(observer, key, deadline.Token)).IsFalse();
            await Assert.That(await IsAvailableAsync(observer, unrelatedKey, deadline.Token)).IsFalse();

            await original.DisposeAsync();
            await original.DisposeAsync();
            await Assert.That(await IsAvailableAsync(observer, key, deadline.Token)).IsEqualTo(!grantBeforeFailure);
            await Assert.That(await IsAvailableAsync(observer, unrelatedKey, deadline.Token)).IsFalse();
            await unrelated.DisposeAsync();
            await Assert.That(await IsAvailableAsync(observer, unrelatedKey, deadline.Token)).IsTrue();
            await Assert.That(connection.State).IsEqualTo(ConnectionState.Open);
            await Assert.That(connection.ProcessID).IsEqualTo(backend);
        }
        finally
        {
            try
            {
                using var restoreTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                if (connection.State == ConnectionState.Open)
                    await SetSearchPathAsync(connection, originalSearchPath, restoreTimeout.Token);
            }
            finally
            {
                try
                {
                    await owner.Database.CloseConnectionAsync();
                }
                finally
                {
                    using var dropTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    setup.CommandText = $"DROP SCHEMA IF EXISTS {quotedSchema} CASCADE";
                    await setup.ExecuteNonQueryAsync(dropTimeout.Token);
                }
            }
        }

        await Assert.That(connection.State).IsEqualTo(ConnectionState.Closed);
        await using var ended = observer.Database.GetDbConnection().CreateCommand();
        ended.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_stat_activity WHERE pid = @backend)";
        ended.Parameters.Add(new NpgsqlParameter("backend", backend));
        while (await ended.ExecuteScalarAsync(deadline.Token) is true)
            await Task.Delay(TimeSpan.FromMilliseconds(20), deadline.Token);
        await Assert.That(await IsAvailableAsync(observer, key, deadline.Token)).IsTrue();
        await Assert.That(await IsAvailableAsync(observer, unrelatedKey, deadline.Token)).IsTrue();
    }

    private static async Task SetSearchPathAsync(NpgsqlConnection connection, string searchPath, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_catalog.set_config('search_path', @path, false)";
        command.Parameters.Add(new NpgsqlParameter("path", searchPath));
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private ExploreDbContext CreateOwner()
    {
        var context = fixture.CreateEnrolledTenantlessDbContext();
        context.BindTenantLease();
        return context;
    }

    private static async Task WaitForOriginalWaiterAsync(
        ExploreDbContext observer, int backend, string resource, Task pending, CancellationToken cancellationToken)
    {
        await observer.Database.OpenConnectionAsync(cancellationToken);
        await using var command = observer.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_locks
                WHERE locktype = 'advisory' AND NOT granted AND pid = @backend
                    AND classid::bigint = ((@key >> 32) & 4294967295)
                    AND objid::bigint = (@key & 4294967295) AND objsubid = 1)
            """;
        command.Parameters.Add(new NpgsqlParameter("backend", backend));
        command.Parameters.Add(new NpgsqlParameter("key", RelationalNamedLock.ComputeStableKey(resource)));
        while (await command.ExecuteScalarAsync(cancellationToken) is not true)
        {
            if (pending.IsCompleted)
            {
                await pending;
                throw new InvalidOperationException("The acquisition completed before its exact advisory wait was observed.");
            }
            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        }
    }

    private static async Task<bool> IsAvailableAsync(
        ExploreDbContext observer, string resource, CancellationToken cancellationToken)
    {
        if (observer.Database.GetDbConnection().State != ConnectionState.Open)
            await observer.Database.OpenConnectionAsync(cancellationToken);
        await using var command = observer.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_catalog.pg_try_advisory_lock(@key)";
        command.Parameters.Add(new NpgsqlParameter("key", RelationalNamedLock.ComputeStableKey(resource)));
        if (await command.ExecuteScalarAsync(cancellationToken) is not true)
            return false;
        command.CommandText = "SELECT pg_catalog.pg_advisory_unlock(@key)";
        await Assert.That(await command.ExecuteScalarAsync(cancellationToken)).IsEqualTo(true);
        return true;
    }
}
