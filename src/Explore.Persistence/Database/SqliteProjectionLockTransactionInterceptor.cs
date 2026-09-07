using System.Collections.Concurrent;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Explore.Persistence.Database;

internal sealed class SqliteProjectionLockTransactionInterceptor : DbTransactionInterceptor
{
    private static readonly ConcurrentDictionary<string, ProjectionLockState> Locks = new(StringComparer.Ordinal);
    private readonly ConditionalWeakTable<DbTransaction, HashSet<string>> _resourcesByTransaction = new();

    public static SqliteProjectionLockTransactionInterceptor Instance { get; } = new();

    public bool TryAcquire(DbTransaction transaction, string resource)
    {
        ProjectionLockState state = Locks.GetOrAdd(resource, static _ => new ProjectionLockState());
        if (!state.TryAcquire(transaction))
        {
            return false;
        }

        HashSet<string> resources = _resourcesByTransaction.GetValue(
            transaction,
            static _ => new HashSet<string>(StringComparer.Ordinal));
        lock (resources)
        {
            resources.Add(resource);
        }

        return true;
    }

    public bool TryProbe(string resource)
    {
        ProjectionLockState state = Locks.GetOrAdd(resource, static _ => new ProjectionLockState());
        return state.TryProbe();
    }

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        Release(transaction);

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) =>
        Release(transaction);

    public override void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData) =>
        Release(transaction);

    public override Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Release(transaction);
        return Task.CompletedTask;
    }

    public override Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Release(transaction);
        return Task.CompletedTask;
    }

    public override Task TransactionFailedAsync(
        DbTransaction transaction,
        TransactionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Release(transaction);
        return Task.CompletedTask;
    }

    private void Release(DbTransaction transaction)
    {
        if (!_resourcesByTransaction.TryGetValue(transaction, out HashSet<string>? resources))
        {
            return;
        }
        _resourcesByTransaction.Remove(transaction);

        string[] snapshot;
        lock (resources)
        {
            snapshot = resources.ToArray();
        }

        foreach (string resource in snapshot)
        {
            if (Locks.TryGetValue(resource, out ProjectionLockState? state))
            {
                state.Release(transaction);
            }
        }
    }

    private sealed class ProjectionLockState
    {
        private readonly object _gate = new();
        private DbTransaction? _owner;

        public bool TryAcquire(DbTransaction transaction)
        {
            lock (_gate)
            {
                ReapDisposedOwner();
                if (_owner is not null && !ReferenceEquals(_owner, transaction))
                {
                    return false;
                }

                _owner = transaction;
                return true;
            }
        }

        public bool TryProbe()
        {
            lock (_gate)
            {
                ReapDisposedOwner();
                return _owner is null;
            }
        }

        public void Release(DbTransaction transaction)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_owner, transaction))
                {
                    _owner = null;
                }
            }
        }

        private void ReapDisposedOwner()
        {
            if (_owner?.Connection is null)
            {
                _owner = null;
            }
        }
    }
}
