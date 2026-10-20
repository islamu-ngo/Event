using System.Data;
using System.Runtime.CompilerServices;
using System.Transactions;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Explore.Persistence.Security.ProviderPrimitives;

/// <summary>
/// Owns one fixed cooperative source resource on the original application connector.
/// No relation privileges, transaction ownership, or arbitrary SQL cross this boundary.
/// </summary>
internal sealed class HumanSecuritySourceGateCustody : IAsyncDisposable
{
    private const string Resource = "human-security-source";
    private static readonly ConditionalWeakTable<ExploreDbContext, ContextSlot> Contexts = new();
    private readonly ExploreDbContext _context;
    private readonly ContextSlot _slot;
    private readonly DbContextId _contextId;
    private readonly Guid? _tenant;
    private readonly string? _provider;
    private readonly RelationalConnectionIdentity _connection;
    private readonly CancellationTokenSource _abandonment = new();
    private readonly object _lifecycle = new();
    private Task<IAsyncDisposable>? _acquisition;
    private Task? _cleanup;
    private IAsyncDisposable? _session;
    private int? _backend;
    private bool _openedConnection;
    private bool _openCompleted;
    private bool _monitorAttached;
    private bool _cancellationDisposed;
    private int _opening;
    private int _interrupted;
    private int _reopened;
    private int _acquired;
    private int _disposed;

    private HumanSecuritySourceGateCustody(ExploreDbContext context, ContextSlot slot)
    {
        _context = context;
        _slot = slot;
        _contextId = context.ContextId;
        _tenant = context.TenantFilterTenantId;
        _provider = context.Database.ProviderName;
        _connection = RelationalConnectionIdentity.Capture(context);
    }

    internal static HumanSecuritySourceGateCustody Create(ExploreDbContext context)
    {
        RequireIdle(context);
        var slot = Contexts.GetValue(context, static _ => new ContextSlot());
        lock (slot)
        {
            if (slot.Custody is not null)
                throw new InvalidOperationException("human_security_source_gate_reentry");
            var custody = new HumanSecuritySourceGateCustody(context, slot);
            slot.Custody = custody;
            return custody;
        }
    }

    internal Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        lock (_lifecycle)
        {
            if (_acquisition is not null)
                throw new InvalidOperationException("human_security_source_gate_reentry");
            _acquisition = AcquireCoreAsync(cancellationToken);
            return _acquisition;
        }
    }

    internal static void RequireCurrent(ExploreDbContext context)
    {
        if (!Contexts.TryGetValue(context, out var slot))
            throw new InvalidOperationException("human_security_source_gate_missing");
        HumanSecuritySourceGateCustody custody;
        lock (slot)
            custody = slot.Custody ?? throw new InvalidOperationException("human_security_source_gate_missing");
        if (Volatile.Read(ref custody._disposed) != 0 || Volatile.Read(ref custody._acquired) == 0)
            throw new InvalidOperationException("human_security_source_gate_inactive");
        custody.RequireConnector();
    }

    internal static bool TryRequireCurrent(ExploreDbContext context)
    {
        if (!Contexts.TryGetValue(context, out var slot))
            return false;
        lock (slot)
        {
            if (slot.Custody is null)
                return false;
            RequireCurrent(context);
            return true;
        }
    }

    private async Task<IAsyncDisposable> AcquireCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _abandonment.Token);
            linked.Token.ThrowIfCancellationRequested();
            RequireIdle(_context);
            RequireOwner();
            if (_connection.State is not (ConnectionState.Open or ConnectionState.Closed))
                throw new InvalidOperationException("human_security_source_connector_unavailable");
            bool opening = _connection.State == ConnectionState.Closed;
            if (opening)
                Volatile.Write(ref _opening, 1);
            _connection.StateChange += OnStateChange;
            _monitorAttached = true;
            if (opening)
            {
                _openedConnection = true;
                await _context.Database.OpenConnectionAsync(linked.Token).ConfigureAwait(false);
                _openCompleted = true;
            }
            else
                _backend = _connection.BackendProcessId;
            RequireConnector();
            _session = await RelationalNamedLock.AcquireSessionAsync(_context, Resource, linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            RequireIdle(_context);
            RequireConnector();
            Volatile.Write(ref _acquired, 1);
            return this;
        }
        catch (Exception acquisitionFailure)
        {
            try
            {
                await CleanupAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                throw new InvalidOperationException("human_security_source_acquisition_cleanup_failed",
                    new AggregateException(acquisitionFailure, cleanupFailure));
            }
            throw;
        }
    }

    private static void RequireIdle(ExploreDbContext context)
    {
        context.RequireBoundTenantLease();
        if (!context.Database.IsRelational())
            throw new InvalidOperationException("human_security_source_requires_relational_owner");
        if (Transaction.Current is not null || context.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("human_security_source_requires_pre_begin_owner");
    }

    private void RequireOwner()
    {
        _context.RequireBoundTenantLease();
        if (_context.ContextId != _contextId || _context.TenantFilterTenantId != _tenant)
            throw new InvalidOperationException("human_security_source_owner_changed");
    }

    private void RequireConnector()
    {
        RequireOwner();
        if (!_connection.Matches(_context) || _connection.State != ConnectionState.Open
            || Volatile.Read(ref _interrupted) != 0 || _connection.BackendProcessId != _backend)
            throw new InvalidOperationException("human_security_source_connector_changed");
    }

    private void OnStateChange(object? sender, StateChangeEventArgs change)
    {
        if (Volatile.Read(ref _opening) != 0)
        {
            if (change.OriginalState == ConnectionState.Closed && change.CurrentState == ConnectionState.Connecting)
                return;
            if (change.OriginalState is ConnectionState.Closed or ConnectionState.Connecting
                && change.CurrentState == ConnectionState.Open && HasOwnerAndConnection()
                && _connection.State == ConnectionState.Open && Interlocked.Exchange(ref _opening, 0) != 0)
            {
                _backend = _connection.BackendProcessId;
                return;
            }
            Interlocked.Exchange(ref _opening, 0);
            Interlocked.Exchange(ref _interrupted, 1);
        }
        if (change.CurrentState != ConnectionState.Open)
            Interlocked.Exchange(ref _interrupted, 1);
        else
            Interlocked.Exchange(ref _reopened, 1);
    }

    private bool HasOwnerAndConnection()
    {
        try { RequireOwner(); return _connection.Matches(_context); }
        catch (InvalidOperationException) { return false; }
    }

    private bool HasConnector()
    {
        try { RequireConnector(); return true; }
        catch (InvalidOperationException) { return false; }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            lock (_lifecycle)
                if (!_cancellationDisposed)
                    _abandonment.Cancel();
        }
        Task<IAsyncDisposable>? acquisition;
        lock (_lifecycle)
            acquisition = _acquisition;
        if (acquisition is not null)
        {
            try { await acquisition.ConfigureAwait(false); }
            catch (Exception)
            {
                // The acquisition caller observes its own failure; disposal joins the same cleanup.
            }
        }
        await CleanupAsync().ConfigureAwait(false);
    }

    private Task CleanupAsync()
    {
        lock (_lifecycle)
            return _cleanup ??= CleanupCoreAsync();
    }

    private async Task CleanupCoreAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        bool lost = false;
        bool releaseAttempted = false;
        Exception? releaseFailure = null;
        try
        {
            if (_session is not null)
            {
                bool original = HasConnector();
                // The SQLite process semaphore belongs to this lease, never to a replacement connection.
                if (_provider == RelationalNamedLock.SqliteProvider || original)
                {
                    releaseAttempted = original;
                    await _session.DisposeAsync().ConfigureAwait(false);
                }
                lost = !original;
            }
        }
        catch (Exception failure)
        {
            releaseFailure = failure;
            throw;
        }
        finally
        {
            try
            {
                bool balanceClosed = (releaseAttempted || _session is null)
                    && Volatile.Read(ref _reopened) == 0 && HasOwnerAndConnection()
                    && _connection.State == ConnectionState.Closed;
                bool close = _openedConnection && (HasConnector() || balanceClosed);
                if (_openedConnection && !close && _monitorAttached)
                    lost = true;
                if (close)
                {
                    try
                    {
                        // An opening interceptor may fail before EF records its owned-open reference.
                        if (!_openCompleted && HasConnector())
                            await _connection.Connection.CloseAsync().ConfigureAwait(false);
                        if (HasConnector() || (HasOwnerAndConnection()
                            && _connection.State == ConnectionState.Closed && Volatile.Read(ref _reopened) == 0))
                            await _context.Database.CloseConnectionAsync().ConfigureAwait(false);
                        else
                            lost = true;
                    }
                    catch (Exception) when (releaseFailure is not null)
                    {
                        // Preserve the original release error if EF reference balancing also fails.
                    }
                }
            }
            finally
            {
                if (_monitorAttached)
                    _connection.StateChange -= OnStateChange;
                lock (_slot)
                    if (ReferenceEquals(_slot.Custody, this))
                        _slot.Custody = null;
                lock (_lifecycle)
                {
                    _cancellationDisposed = true;
                    _abandonment.Dispose();
                }
            }
        }
        if (lost)
            throw new InvalidOperationException("human_security_source_cleanup_lost_custody");
    }

    private sealed class ContextSlot
    {
        internal HumanSecuritySourceGateCustody? Custody { get; set; }
    }
}
