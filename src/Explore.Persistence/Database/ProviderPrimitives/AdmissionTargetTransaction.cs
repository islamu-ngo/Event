using System.Data;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Database.ProviderPrimitives;

/// <summary>Orders participating target workflows before their transaction snapshot is established.</summary>
public sealed class AdmissionTargetTransaction(ExploreDbContext context, ISerializableAttemptOwner attemptOwner)
    : IAdmissionTargetTransaction
{
    private static readonly ConditionalWeakTable<ExploreDbContext, Slot> Owners = new();

    public async Task<T> ExecuteAsync<T>(Guid tenantId, Guid targetId,
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        context.RequireBoundTenantLease();
        if (tenantId == Guid.Empty || targetId == Guid.Empty || context.TenantFilterTenantId != tenantId)
            throw new UnauthorizedAccessException("admission_target_owner_mismatch");
        if (!context.Database.IsRelational() || context.Database.CurrentTransaction is not null
            || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("admission_target_requires_pre_begin_owner");
        var slot = Owners.GetValue(context, static _ => new Slot());
        var custody = new Custody(context, tenantId, targetId);
        lock (slot)
        {
            if (slot.Owner is not null) throw new InvalidOperationException("admission_target_owner_reentry");
            slot.Owner = custody;
        }
        T result = default!;
        Exception? primary = null;
        bool acknowledged = false;
        try
        {
            await custody.AcquireAsync(cancellationToken);
            result = await attemptOwner.ExecuteAsync(async token =>
            {
                custody.BindTransaction();
                var value = await operation(token);
                custody.RequireCurrent(transactionRequired: true);
                return value;
            }, cancellationToken);
            acknowledged = true;
            custody.RequireCurrent();
        }
        catch (Exception failure)
        {
            primary = acknowledged
                ? new InvalidOperationException("admission_target_committed_cleanup_failed", failure) : failure;
        }
        try { await custody.DisposeAsync(); }
        catch (Exception cleanup)
        {
            if (primary is HumanMutationOutcomeUnknownException)
                throw new HumanMutationOutcomeUnknownException(new AggregateException(primary, cleanup));
            if (acknowledged)
                throw new InvalidOperationException("admission_target_committed_cleanup_failed",
                    primary is null ? cleanup : new AggregateException(primary, cleanup));
            if (primary is not null) throw new AggregateException(primary, cleanup);
            throw;
        }
        finally
        {
            lock (slot)
                if (ReferenceEquals(slot.Owner, custody)) slot.Owner = null;
        }
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
        return result;
    }

    internal static void RequireCurrent(ExploreDbContext context, Guid tenantId, Guid targetId)
    {
        if (!Owners.TryGetValue(context, out var slot))
            throw new InvalidOperationException("admission_target_owner_missing");
        lock (slot)
        {
            var owner = slot.Owner ?? throw new InvalidOperationException("admission_target_owner_missing");
            if (owner.TenantId != tenantId || owner.TargetId != targetId)
                throw new UnauthorizedAccessException("admission_target_owner_mismatch");
            owner.RequireCurrent(transactionRequired: true);
        }
    }

    internal static async Task RequireCurrentAsync(ExploreDbContext context, Guid tenantId, Guid targetId,
        CancellationToken cancellationToken)
    {
        RequireCurrent(context, tenantId, targetId);
        if (context.Database.ProviderName == RelationalNamedLock.SqliteProvider)
            await RelationalEntityRowFence.AcquireAsync<AdmissionTarget>(context, tenantId,
                target => target.Id, targetId, cancellationToken);
    }

    internal static void RequireCurrentIfHeld(ExploreDbContext context)
    {
        if (!Owners.TryGetValue(context, out var slot)) return;
        lock (slot) slot.Owner?.RequireCurrent(transactionRequired: true);
    }

    private sealed class Slot { internal Custody? Owner { get; set; } }

    private sealed class Custody(ExploreDbContext context, Guid tenantId, Guid targetId) : IAsyncDisposable
    {
        private readonly RelationalConnectionIdentity connection = RelationalConnectionIdentity.Capture(context);
        private readonly DbContextId contextId = context.ContextId;
        private IAsyncDisposable? session;
        private int? backend;
        private int changed;
        private int reopened;
        private int initialOpenPending;
        private bool opened;
        private bool initialOpenCompleted;
        private bool observing;
        private System.Data.Common.DbTransaction? transaction;
        internal Guid TenantId => tenantId;
        internal Guid TargetId => targetId;

        internal async Task AcquireAsync(CancellationToken cancellationToken)
        {
            if (connection.State is not (ConnectionState.Open or ConnectionState.Closed))
                throw new InvalidOperationException("admission_target_connector_unavailable");
            opened = connection.State == ConnectionState.Closed;
            if (opened) Volatile.Write(ref initialOpenPending, 1);
            else backend = connection.BackendProcessId;
            connection.StateChange += Changed;
            observing = true;
            if (opened)
            {
                await context.Database.OpenConnectionAsync(cancellationToken);
                initialOpenCompleted = true;
            }
            session = await RelationalNamedLock.AcquireSessionAsync(context,
                $"admission-target:{tenantId:N}:{targetId:N}", cancellationToken);
            RequireCurrent();
        }

        internal void BindTransaction()
        {
            RequireCurrent();
            transaction = context.Database.CurrentTransaction?.GetDbTransaction()
                ?? throw new InvalidOperationException("admission_target_transaction_missing");
            RequireCurrent(transactionRequired: true);
        }

        internal void RequireCurrent(bool transactionRequired = false)
        {
            context.RequireBoundTenantLease();
            if (session is null || context.ContextId != contextId || context.TenantFilterTenantId != tenantId
                || Volatile.Read(ref changed) != 0 || !connection.Matches(context)
                || connection.State != ConnectionState.Open || connection.BackendProcessId != backend)
                throw new InvalidOperationException("admission_target_owner_changed");
            if (transactionRequired && (transaction is null
                || !ReferenceEquals(transaction, context.Database.CurrentTransaction?.GetDbTransaction())
                || !connection.Owns(transaction)))
                throw new InvalidOperationException("admission_target_transaction_changed");
        }

        private void Changed(object? sender, StateChangeEventArgs args)
        {
            if (Volatile.Read(ref initialOpenPending) != 0)
            {
                if (args.OriginalState == ConnectionState.Closed && args.CurrentState == ConnectionState.Connecting)
                    return;
                if (args.OriginalState is ConnectionState.Closed or ConnectionState.Connecting
                    && args.CurrentState == ConnectionState.Open && connection.Matches(context)
                    && Interlocked.Exchange(ref initialOpenPending, 0) != 0)
                {
                    backend = connection.BackendProcessId;
                    return;
                }
                Interlocked.Exchange(ref initialOpenPending, 0);
                Interlocked.Exchange(ref changed, 1);
            }
            if (args.CurrentState == ConnectionState.Open)
                Interlocked.Exchange(ref reopened, 1);
            else
                Interlocked.Exchange(ref changed, 1);
        }

        public async ValueTask DisposeAsync()
        {
            Exception? failure = null;
            try
            {
                if (session is not null)
                {
                    // SQLite's lease is a process semaphore and must be released even
                    // after connection loss; native leases must not touch a replacement session.
                    if (context.Database.ProviderName != RelationalNamedLock.SqliteProvider) RequireCurrent();
                    await session.DisposeAsync();
                }
            }
            catch (Exception releaseFailure) { failure = releaseFailure; }
            finally
            {
                try
                {
                    bool original = context.ContextId == contextId && connection.Matches(context)
                        && Volatile.Read(ref reopened) == 0;
                    bool canClose = opened && original && (connection.State == ConnectionState.Closed
                        || connection.State == ConnectionState.Open && Volatile.Read(ref changed) == 0
                            && connection.BackendProcessId == backend);
                    if (canClose)
                    {
                        if (!initialOpenCompleted && connection.State == ConnectionState.Open)
                            await connection.Connection.CloseAsync();
                        if (connection.Matches(context) && Volatile.Read(ref reopened) == 0)
                            await context.Database.CloseConnectionAsync();
                    }
                    else if (opened)
                        throw new InvalidOperationException("admission_target_cleanup_lost_custody");
                }
                catch (Exception closeFailure)
                {
                    failure = failure is null ? closeFailure : new AggregateException(failure, closeFailure);
                }
                finally
                {
                    if (observing) connection.StateChange -= Changed;
                }
            }
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
