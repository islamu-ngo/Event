using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Explore.Persistence.Database;

internal sealed class MySqlNamedLockTransactionInterceptor : DbTransactionInterceptor, IDbConnectionInterceptor
{
    private readonly ConcurrentDictionary<DbTransaction, HashSet<string>> _locks = new();

    public static MySqlNamedLockTransactionInterceptor Instance { get; } = new();

    public bool IsTracked(DbTransaction transaction, string resource)
    {
        return _locks.TryGetValue(transaction, out HashSet<string>? resources)
            && Contains(resources, resource);
    }

    public void Track(DbTransaction transaction, string resource)
    {
        HashSet<string> resources = _locks.GetOrAdd(transaction, static _ => new(StringComparer.Ordinal));
        lock (resources)
        {
            resources.Add(resource);
        }
    }

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        ReleaseTracked(transaction);

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) =>
        ReleaseTracked(transaction);

    public override void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData) =>
        ReleaseTracked(transaction);

    public override Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default) => ReleaseAsync(transaction);

    public override Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default) => ReleaseAsync(transaction);

    public override Task TransactionFailedAsync(
        DbTransaction transaction,
        TransactionErrorEventData eventData,
        CancellationToken cancellationToken = default) => ReleaseAsync(transaction);

    public InterceptionResult ConnectionClosing(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        foreach (DbTransaction transaction in TransactionsFor(connection))
        {
            ReleaseTracked(transaction);
        }

        return result;
    }

    public async ValueTask<InterceptionResult> ConnectionClosingAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        foreach (DbTransaction transaction in TransactionsFor(connection))
        {
            await ReleaseAsync(transaction).ConfigureAwait(false);
        }

        return result;
    }

    private static bool Contains(HashSet<string> resources, string resource)
    {
        lock (resources)
        {
            return resources.Contains(resource);
        }
    }

    private DbTransaction[] TransactionsFor(DbConnection connection) =>
        _locks.Keys.Where(transaction => ReferenceEquals(transaction.Connection, connection)).ToArray();

    internal void ReleaseTracked(DbTransaction transaction)
    {
        if (!_locks.TryRemove(transaction, out HashSet<string>? resources))
        {
            return;
        }

        DbConnection? connection = transaction.Connection;
        if (connection is null || connection.State != ConnectionState.Open)
        {
            return;
        }

        try
        {
            foreach (string resource in Snapshot(resources))
            {
                RelationalNamedLock.ReleaseMySql(connection, resource);
            }
        }
        catch
        {
            connection.Close();
            throw;
        }
    }

    private async Task ReleaseAsync(DbTransaction transaction)
    {
        if (!_locks.TryRemove(transaction, out HashSet<string>? resources))
        {
            return;
        }

        DbConnection? connection = transaction.Connection;
        if (connection is null || connection.State != ConnectionState.Open)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            foreach (string resource in Snapshot(resources))
            {
                await RelationalNamedLock.ReleaseMySqlAsync(connection, resource, timeout.Token)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            await connection.CloseAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static string[] Snapshot(HashSet<string> resources)
    {
        lock (resources)
        {
            return resources.OrderBy(static resource => resource, StringComparer.Ordinal).ToArray();
        }
    }
}
