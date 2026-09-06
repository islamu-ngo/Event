// ABOUTME: Provides provider-neutral transaction and session named locks for relational persistence coordination.
// ABOUTME: Preserves server-side cross-instance locking and uses a process semaphore only for single-instance SQLite.

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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

        DbConnection connection = dbContext.Database.GetDbConnection();
        bool ownsConnection = connection.State != ConnectionState.Open;
        if (ownsConnection)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        string providerResource = NormalizeProviderResource(providerName, resource);
        try
        {
            await using DbCommand command = CreateAcquireCommand(
                connection,
                transaction: null,
                providerName,
                providerResource,
                transactionOwner: false);
            object? result = providerName == PostgreSqlProvider
                ? await ExecuteNonQueryAsync(command, cancellationToken).ConfigureAwait(false)
                : await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            EnsureAcquireSucceeded(providerName, result);
            return new SessionLease(connection, providerName, providerResource, ownsConnection);
        }
        catch
        {
            if (connection.State != ConnectionState.Closed)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }

            throw;
        }
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
                if (connection.State != ConnectionState.Closed)
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
                if (ownsConnection && connection.State != ConnectionState.Closed)
                {
                    await connection.CloseAsync().ConfigureAwait(false);
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
