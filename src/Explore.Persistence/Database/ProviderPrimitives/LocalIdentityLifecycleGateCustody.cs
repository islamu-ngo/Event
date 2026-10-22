using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Transactions;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace Explore.Persistence.Database.ProviderPrimitives;

/// <summary>Retains the configured identity connector and one fixed cooperative lifecycle resource.</summary>
internal sealed class LocalIdentityLifecycleGateCustody : IAsyncDisposable
{
    private const string Resource = "local-identity-current-eligibility";
    private static readonly ConditionalWeakTable<DbContext, Slot> Slots = new();
    private readonly DbContext context;
    private readonly DbContextId contextId;
    private readonly Guid? tenantId;
    private readonly DbConnection connection;
    private readonly Slot slot;
    private readonly bool sqlite;
    private readonly CancellationTokenSource abandonment = new();
    private readonly object lifecycle = new();
    private Task? acquisition;
    private Task? cleanup;
    private IAsyncDisposable? session;
    private bool opened;
    private bool openCompleted;
    private bool monitoring;
    private bool cancellationDisposed;
    private int firstOpen;
    private int changed;
    private int reopened;
    private int disposed;
    private int acquired;
    private int? backend;

    private LocalIdentityLifecycleGateCustody(DbContext context, Slot slot)
    {
        this.context = context;
        this.slot = slot;
        contextId = context.ContextId;
        tenantId = (context as ExploreDbContext)?.TenantFilterTenantId;
        connection = context.Database.GetDbConnection();
        sqlite = context.Database.ProviderName == RelationalNamedLock.SqliteProvider;
    }

    internal static LocalIdentityLifecycleGateCustody Create(DbContext context)
    {
        RequireIdle(context);
        var slot = Slots.GetValue(context, static _ => new Slot());
        lock (slot)
        {
            if (slot.Owner is not null) throw new InvalidOperationException("local_identity_lifecycle_gate_reentry");
            var owner = new LocalIdentityLifecycleGateCustody(context, slot);
            slot.Owner = owner;
            return owner;
        }
    }

    internal Task AcquireAsync(CancellationToken cancellationToken)
    {
        lock (lifecycle)
        {
            if (acquisition is not null) throw new InvalidOperationException("local_identity_lifecycle_gate_reentry");
            return acquisition = AcquireCoreAsync(cancellationToken);
        }
    }

    internal static async Task<LocalIdentityLifecycleGateCustody> EnterAsync(DbContext context, CancellationToken cancellationToken)
    {
        var custody = Create(context);
        await custody.AcquireAsync(cancellationToken).ConfigureAwait(false);
        return custody;
    }

    internal static void RequireHeld(DbContext context)
    {
        if (!Slots.TryGetValue(context, out var slot))
            throw new UnauthorizedAccessException("The original Local lifecycle fence is absent.");
        lock (slot)
            (slot.Owner ?? throw new UnauthorizedAccessException("The original Local lifecycle fence is absent.")).RequireCurrent();
    }

    internal void RequireCurrent()
    {
        if (Volatile.Read(ref disposed) != 0 || Volatile.Read(ref acquired) == 0)
            throw new UnauthorizedAccessException("The Local identity lifecycle lease is inactive.");
        lock (slot)
            if (!ReferenceEquals(slot.Owner, this))
                throw new UnauthorizedAccessException("The Local identity lifecycle issuer has changed.");
        RequireConnector();
    }

    private async Task AcquireCoreAsync(CancellationToken token)
    {
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, abandonment.Token);
            linked.Token.ThrowIfCancellationRequested();
            RequireIdle(context);
            RequireOwner();
            if (connection.State is not (ConnectionState.Closed or ConnectionState.Open))
                throw new InvalidOperationException("The identity connector is unavailable.");
            bool needsOpen = connection.State == ConnectionState.Closed;
            if (needsOpen) Volatile.Write(ref firstOpen, 1);
            connection.StateChange += OnStateChange;
            monitoring = true;
            if (needsOpen)
            {
                opened = true;
                await context.Database.OpenConnectionAsync(linked.Token).ConfigureAwait(false);
                openCompleted = true;
            }
            else backend = Backend;
            RequireConnector();
            session = await RelationalNamedLock.AcquireSessionAsync(context, Resource, linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            RequireIdle(context);
            RequireConnector();
            Volatile.Write(ref acquired, 1);
        }
        catch
        {
            await CleanupAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static void RequireIdle(DbContext context)
    {
        if (context is ExploreDbContext application) application.RequireBoundTenantLease();
        if (!context.Database.IsRelational() || Transaction.Current is not null || context.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("The Local lifecycle fence requires the original pre-BEGIN relational owner.");
    }

    private void RequireOwner()
    {
        if (context is ExploreDbContext application)
        {
            application.RequireBoundTenantLease();
            if (application.TenantFilterTenantId != tenantId)
                throw new UnauthorizedAccessException("The original Local identity tenant selection has changed.");
        }
        if (context.ContextId != contextId || !ReferenceEquals(context.Database.GetDbConnection(), connection))
            throw new UnauthorizedAccessException("The Local identity lifecycle owner has changed.");
    }

    private int? Backend => connection is NpgsqlConnection postgres ? postgres.ProcessID : null;

    private void RequireConnector()
    {
        RequireOwner();
        if (connection.State != ConnectionState.Open || Volatile.Read(ref changed) != 0 || Backend != backend)
            throw new UnauthorizedAccessException("The Local identity lifecycle connector has changed.");
    }

    private bool OwnsConnection()
    {
        try { RequireOwner(); return true; }
        catch (InvalidOperationException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private bool OwnsConnector()
    {
        try { RequireConnector(); return true; }
        catch (InvalidOperationException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private void OnStateChange(object? sender, StateChangeEventArgs change)
    {
        if (Volatile.Read(ref firstOpen) != 0)
        {
            if (change.OriginalState == ConnectionState.Closed && change.CurrentState == ConnectionState.Connecting) return;
            if (change.OriginalState is ConnectionState.Closed or ConnectionState.Connecting
                && change.CurrentState == ConnectionState.Open && OwnsConnection() && connection.State == ConnectionState.Open
                && Interlocked.Exchange(ref firstOpen, 0) != 0)
            {
                backend = Backend;
                return;
            }
            Interlocked.Exchange(ref firstOpen, 0);
            Interlocked.Exchange(ref changed, 1);
        }
        if (change.CurrentState != ConnectionState.Open) Interlocked.Exchange(ref changed, 1);
        else Interlocked.Exchange(ref reopened, 1);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
            lock (lifecycle)
                if (!cancellationDisposed) abandonment.Cancel();
        Task? pending;
        lock (lifecycle) pending = acquisition;
        if (pending is not null)
            try { await pending.ConfigureAwait(false); }
            catch (Exception) { /* Acquisition delivers its own failure; still join physical cleanup. */ }
        await CleanupAsync().ConfigureAwait(false);
    }

    private Task CleanupAsync()
    {
        lock (lifecycle) return cleanup ??= CleanupCoreAsync();
    }

    private async Task CleanupCoreAsync()
    {
        Interlocked.Exchange(ref disposed, 1);
        bool lost = false, releaseOnOriginal = false;
        Exception? releaseFailure = null;
        try
        {
            if (session is not null)
            {
                bool original = OwnsConnector();
                if (sqlite || original)
                {
                    releaseOnOriginal = original;
                    await session.DisposeAsync().ConfigureAwait(false);
                }
                lost = !original;
            }
        }
        catch (Exception failure) { releaseFailure = failure; throw; }
        finally
        {
            try
            {
                bool balanceClosed = (releaseOnOriginal || session is null) && Volatile.Read(ref reopened) == 0
                    && OwnsConnection() && connection.State == ConnectionState.Closed;
                bool close = opened && (OwnsConnector() || balanceClosed);
                if (opened && !close && monitoring) lost = true;
                if (close)
                {
                    try
                    {
                        if (!openCompleted && OwnsConnector()) await connection.CloseAsync().ConfigureAwait(false);
                        if (OwnsConnector() || (OwnsConnection() && connection.State == ConnectionState.Closed
                            && Volatile.Read(ref reopened) == 0))
                            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
                        else lost = true;
                    }
                    catch (Exception) when (releaseFailure is not null) { /* Preserve the original release failure. */ }
                }
            }
            finally
            {
                if (monitoring) connection.StateChange -= OnStateChange;
                lock (slot) if (ReferenceEquals(slot.Owner, this)) slot.Owner = null;
                lock (lifecycle) { cancellationDisposed = true; abandonment.Dispose(); }
            }
        }
        if (lost) throw new UnauthorizedAccessException("Local identity lifecycle cleanup lost its original connector.");
    }

    private sealed class Slot { internal LocalIdentityLifecycleGateCustody? Owner { get; set; } }
}
