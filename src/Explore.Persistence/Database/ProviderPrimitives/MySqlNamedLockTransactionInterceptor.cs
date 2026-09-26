using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Explore.Persistence.Database;

internal sealed class MySqlNamedLockTransactionInterceptor : DbTransactionInterceptor, IDbConnectionInterceptor
{
    private readonly ConcurrentDictionary<DbTransaction, (DbConnection Connection, HashSet<string> Resources)> _locks = new();

    public static MySqlNamedLockTransactionInterceptor Instance { get; } = new();

    public bool IsTracked(DbTransaction transaction, string resource)
    {
        return _locks.TryGetValue(transaction, out var locks)
            && Contains(locks.Resources, resource);
    }

    public void Track(DbTransaction transaction, string resource)
    {
        var locks = _locks.GetOrAdd(transaction, static owner => (
            Connection: owner.Connection ?? throw new InvalidOperationException("Named locks require an active transaction."),
            Resources: new HashSet<string>(StringComparer.Ordinal)));
        lock (locks.Resources)
        {
            locks.Resources.Add(resource);
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
        _locks.Where(entry => ReferenceEquals(entry.Value.Connection, connection))
            .Select(entry => entry.Key)
            .ToArray();

    internal void ReleaseTracked(DbTransaction transaction)
    {
        if (!_locks.TryRemove(transaction, out var locks))
        {
            return;
        }

        DbConnection connection = locks.Connection;
        if (connection.State != ConnectionState.Open)
        {
            return;
        }

        try
        {
            foreach (string resource in Snapshot(locks.Resources))
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
        if (!_locks.TryRemove(transaction, out var locks))
        {
            return;
        }

        DbConnection connection = locks.Connection;
        if (connection.State != ConnectionState.Open)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            foreach (string resource in Snapshot(locks.Resources))
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
