using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Explore.Persistence.Database;

internal static class RelationalNamedLock
{
    internal const string PostgreSqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";
    internal const string SqlServerProvider = "Microsoft.EntityFrameworkCore.SqlServer";
    internal const string SqliteProvider = "Microsoft.EntityFrameworkCore.Sqlite";
    internal const string MySqlProvider = "Microting.EntityFrameworkCore.MySql";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SqliteLocks =
        new(StringComparer.Ordinal);

    public static async Task<IAsyncDisposable> AcquireTransactionAsync(
        DbContext dbContext,
        string resource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);

        if (!dbContext.Database.IsRelational())
        {
            return NoopLease.Instance;
        }

        DbTransaction transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("Named-lock operations require an active relational transaction.");
        string providerName = RequireSupportedProvider(dbContext.Database.ProviderName);

        if (providerName == SqliteProvider)
        {
            string sqliteResource = resource.Trim();
            SqliteNamedLockTransactionInterceptor.Instance.ReleaseCompletedTransactionsFor(
                dbContext.Database.GetDbConnection());
            if (SqliteNamedLockTransactionInterceptor.Instance.IsTracked(
                    transaction,
                    sqliteResource))
            {
                return NoopLease.Instance;
            }

            SemaphoreSlim semaphore = await AcquireSqliteProcessLockAsync(
                sqliteResource,
                cancellationToken).ConfigureAwait(false);
            try
            {
                SqliteNamedLockTransactionInterceptor.Instance.Track(
                    transaction,
                    sqliteResource,
                    semaphore);
            }
            catch
            {
                semaphore.Release();
                throw;
            }

            return NoopLease.Instance;
        }

        string providerResource = NormalizeProviderResource(providerName, resource);
        if (providerName == MySqlProvider
            && MySqlNamedLockTransactionInterceptor.Instance.IsTracked(transaction, providerResource))
        {
            return NoopLease.Instance;
        }

        DbConnection connection = dbContext.Database.GetDbConnection();
        await using DbCommand command = CreateAcquireCommand(
            connection,
            transaction,
            providerName,
            providerResource,
            transactionOwner: true);
        object? result;
        try
        {
            result = providerName == PostgreSqlProvider
                ? await ExecuteNonQueryAsync(command, cancellationToken).ConfigureAwait(false)
                : await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            EnsureAcquireSucceeded(providerName, result);
        }
        catch when (providerName == MySqlProvider)
        {
            await connection.CloseAsync().ConfigureAwait(false);
            throw;
        }

        if (providerName == MySqlProvider)
        {
            MySqlNamedLockTransactionInterceptor.Instance.Track(transaction, providerResource);
        }

        return NoopLease.Instance;
    }

    public static async Task<IAsyncDisposable> AcquireSessionAsync(
        DbContext dbContext,
        string resource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        cancellationToken.ThrowIfCancellationRequested();

        if (!dbContext.Database.IsRelational())
        {
            return NoopLease.Instance;
        }

        string providerName = RequireSupportedProvider(dbContext.Database.ProviderName);
        if (providerName == SqliteProvider)
        {
            // ponytail: process-only lock assumes documented single-instance SQLite; use a lock service if SQLite becomes multi-instance.
            SemaphoreSlim semaphore = await AcquireSqliteProcessLockAsync(
                resource,
                cancellationToken).ConfigureAwait(false);
            return new SemaphoreLease(semaphore);
        }

        var originalContextId = dbContext.ContextId;
        DbConnection connection = dbContext.Database.GetDbConnection();
        bool ownsConnection = connection.State != ConnectionState.Open;
        if (ownsConnection)
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        string providerResource = NormalizeProviderResource(providerName, resource);
        var postgres = connection as NpgsqlConnection;
        int? originalBackend = postgres?.ProcessID;
        string originalDatabase = connection.Database;
        int connectorChanged = 0;
        bool acquisitionStarted = false;
        bool previouslyHeld = false;
        void ObserveConnectorChange(object? sender, StateChangeEventArgs args) =>
            Interlocked.Exchange(ref connectorChanged, 1);
        bool IsOriginalPostgresSession()
        {
            try
            {
                return postgres is not null && originalBackend.HasValue
                    && Volatile.Read(ref connectorChanged) == 0
                    && dbContext.ContextId == originalContextId
                    && ReferenceEquals(dbContext.Database.GetDbConnection(), connection)
                    && connection.State == ConnectionState.Open && postgres.ProcessID == originalBackend
                    && string.Equals(connection.Database, originalDatabase, StringComparison.Ordinal);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
        if (providerName == PostgreSqlProvider)
            connection.StateChange += ObserveConnectorChange;
        try
        {
            if (providerName == PostgreSqlProvider)
            {
                if (!IsOriginalPostgresSession())
                    throw new InvalidOperationException("named_lock_session_custody_lost");
                previouslyHeld = await PostgreSqlSessionAlreadyHoldsAsync(connection,
                    ComputeStableKey(providerResource), originalBackend!.Value, originalDatabase,
                    cancellationToken).ConfigureAwait(false);
                if (!IsOriginalPostgresSession())
                    throw new InvalidOperationException("named_lock_session_custody_lost");
            }
            await using DbCommand command = CreateAcquireCommand(
                connection,
                transaction: null,
                providerName,
                providerResource,
                transactionOwner: false);
            cancellationToken.ThrowIfCancellationRequested();
            acquisitionStarted = true;
            object? result = providerName == PostgreSqlProvider
                ? await ExecuteNonQueryAsync(command, cancellationToken).ConfigureAwait(false)
                : await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            EnsureAcquireSucceeded(providerName, result);
            if (providerName == PostgreSqlProvider && !IsOriginalPostgresSession())
                throw new InvalidOperationException("named_lock_session_custody_lost");
            return new SessionLease(dbContext, connection, providerName, providerResource, ownsConnection);
        }
        catch (Exception acquisitionFailure)
        {
            if (providerName == PostgreSqlProvider)
            {
                if (!IsOriginalPostgresSession())
                    throw new InvalidOperationException("named_lock_session_custody_lost", acquisitionFailure);
                if (!acquisitionStarted || !previouslyHeld)
                {
                    try
                    {
                        if (acquisitionStarted)
                        {
                            // Cancellation can race a grant. Reconcile only this previously-unheld
                            // key; never consume an earlier reentrant lease to guess an outcome.
                            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                            await using DbCommand release = CreateReleaseCommand(connection, providerName, providerResource);
                            object? released = await release.ExecuteScalarAsync(cleanupTimeout.Token).ConfigureAwait(false);
                            if (released is not bool || !IsOriginalPostgresSession())
                                throw new InvalidOperationException("named_lock_acquisition_cleanup_unconfirmed");
                        }
                    }
                    catch (Exception cleanupFailure)
                    {
                        if (IsOriginalPostgresSession())
                        {
                            if (ownsConnection)
                                await dbContext.Database.CloseConnectionAsync().ConfigureAwait(false);
                            else
                                await connection.CloseAsync().ConfigureAwait(false);
                        }
                        throw new InvalidOperationException("named_lock_acquisition_cleanup_failed",
                            new AggregateException(acquisitionFailure, cleanupFailure));
                    }
                    if (ownsConnection)
                        await dbContext.Database.CloseConnectionAsync().ConfigureAwait(false);
                    throw;
                }
            }
            if (ownsConnection)
            {
                await dbContext.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
            else if (connection.State != ConnectionState.Closed)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }

            throw;
        }
        finally
        {
            if (providerName == PostgreSqlProvider)
                connection.StateChange -= ObserveConnectorChange;
        }
    }

    private static async Task<bool> PostgreSqlSessionAlreadyHoldsAsync(
        DbConnection connection, long key, int backend, string database, CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT CASE WHEN pg_catalog.pg_backend_pid() = @backend
                AND pg_catalog.current_database() = @database THEN EXISTS (
                    SELECT 1 FROM pg_catalog.pg_locks held
                    WHERE held.locktype = 'advisory' AND held.granted AND held.pid = @backend
                        AND held.database = (SELECT oid FROM pg_catalog.pg_database WHERE datname = @database)
                        AND held.classid::bigint = ((@key >> 32) & 4294967295)
                        AND held.objid::bigint = (@key & 4294967295) AND held.objsubid = 1)
                ELSE NULL END
            """;
        AddParameter(command, "key", key);
        AddParameter(command, "backend", backend);
        AddParameter(command, "database", database);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is bool held
            ? held : throw new InvalidOperationException("named_lock_session_ownership_unknown");
    }

    internal static string NormalizeProviderResource(string providerName, string resource)
    {
        string trimmed = resource.Trim();
        return providerName == MySqlProvider || providerName == SqlServerProvider && trimmed.Length > 255
            ? "explore:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(trimmed)))[..56]
            : trimmed;
    }

    internal static long ComputeStableKey(string resource)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(resource));
        return BinaryPrimitives.ReadInt64BigEndian(hash);
    }

    private static async Task<SemaphoreSlim> AcquireSqliteProcessLockAsync(
        string resource,
        CancellationToken cancellationToken)
    {
        SemaphoreSlim semaphore = SqliteLocks.GetOrAdd(
            resource.Trim(),
            static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return semaphore;
    }

    internal static void EnsureAcquireSucceeded(string providerName, object? result)
    {
        if (providerName == PostgreSqlProvider)
        {
            return;
        }

        int value = result is null or DBNull ? int.MinValue : Convert.ToInt32(result, CultureInfo.InvariantCulture);
        bool acquired = providerName switch
        {
            SqlServerProvider => value >= 0,
            MySqlProvider => value == 1,
            _ => false,
        };
        if (!acquired)
        {
            throw new InvalidOperationException($"The {providerName} named lock could not be acquired (result {value}).");
        }
    }

    internal static void EnsureReleaseSucceeded(string providerName, object? result)
    {
        bool released = providerName switch
        {
            PostgreSqlProvider => result is true,
            SqlServerProvider => result is not null and not DBNull
                && Convert.ToInt32(result, CultureInfo.InvariantCulture) >= 0,
            MySqlProvider => result is not null and not DBNull
                && Convert.ToInt32(result, CultureInfo.InvariantCulture) == 1,
            _ => false,
        };
        if (!released)
        {
            throw new InvalidOperationException($"The {providerName} named lock could not be released.");
        }
    }

    internal static async Task ReleaseMySqlAsync(
        DbConnection connection,
        string resource,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = CreateReleaseCommand(connection, MySqlProvider, resource);
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        EnsureReleaseSucceeded(MySqlProvider, result);
    }

    internal static void ReleaseMySql(DbConnection connection, string resource)
    {
        using DbCommand command = CreateReleaseCommand(connection, MySqlProvider, resource);
        EnsureReleaseSucceeded(MySqlProvider, command.ExecuteScalar());
    }

    internal static DbCommand CreateAcquireCommand(
        DbConnection connection,
        DbTransaction? transaction,
        string providerName,
        string resource,
        bool transactionOwner)
    {
        DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = providerName switch
        {
            PostgreSqlProvider => transactionOwner
                ? "SELECT pg_advisory_xact_lock(@key)"
                : "SELECT pg_advisory_lock(@key)",
            SqlServerProvider => transactionOwner
                ? "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = -1; SELECT @result;"
                : "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = -1; SELECT @result;",
            MySqlProvider => "SELECT GET_LOCK(@resource, 31536000)",
            _ => throw new InvalidOperationException($"Unsupported relational lock provider '{providerName}'."),
        };
        AddParameter(
            command,
            providerName == PostgreSqlProvider ? "key" : "resource",
            providerName == PostgreSqlProvider ? ComputeStableKey(resource) : resource);
        return command;
    }

    internal static DbCommand CreateReleaseCommand(
        DbConnection connection,
        string providerName,
        string resource)
    {
        DbCommand command = connection.CreateCommand();
        command.CommandText = providerName switch
        {
            PostgreSqlProvider => "SELECT pg_advisory_unlock(@key)",
            SqlServerProvider => "DECLARE @result int; EXEC @result = sys.sp_releaseapplock @Resource = @resource, @LockOwner = 'Session'; SELECT @result;",
            MySqlProvider => "SELECT RELEASE_LOCK(@resource)",
            _ => throw new InvalidOperationException($"Unsupported relational lock provider '{providerName}'."),
        };
        AddParameter(
            command,
            providerName == PostgreSqlProvider ? "key" : "resource",
            providerName == PostgreSqlProvider ? ComputeStableKey(resource) : resource);
        return command;
    }

    private static string RequireSupportedProvider(string? providerName) => providerName switch
    {
        PostgreSqlProvider or SqlServerProvider or SqliteProvider or MySqlProvider => providerName,
        _ => throw new InvalidOperationException($"Unsupported relational lock provider '{providerName}'."),
    };

    private static async Task<object?> ExecuteNonQueryAsync(DbCommand command, CancellationToken cancellationToken)
    {
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return null;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed class SessionLease(
        DbContext dbContext,
        DbConnection connection,
        string providerName,
        string resource,
        bool ownsConnection) : IAsyncDisposable
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (connection.State != ConnectionState.Open)
            {
                if (ownsConnection)
                {
                    await dbContext.Database.CloseConnectionAsync().ConfigureAwait(false);
                }
                else if (connection.State != ConnectionState.Closed)
                {
                    await connection.CloseAsync().ConfigureAwait(false);
                }

                return;
            }

            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await using DbCommand command = CreateReleaseCommand(connection, providerName, resource);
                object? result = await command.ExecuteScalarAsync(timeout.Token).ConfigureAwait(false);
                EnsureReleaseSucceeded(providerName, result);
            }
            catch
            {
                await connection.CloseAsync().ConfigureAwait(false);
                throw;
            }
            finally
            {
                if (ownsConnection)
                {
                    await dbContext.Database.CloseConnectionAsync().ConfigureAwait(false);
                }
            }
        }
    }

    private sealed class SemaphoreLease(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                semaphore.Release();
            }

            return ValueTask.CompletedTask;
        }
    }

    internal sealed class NoopLease : IAsyncDisposable
    {
        public static NoopLease Instance { get; } = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
