using System.Data;
using System.Runtime.CompilerServices;
using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Explore.Persistence.Database.ProviderPrimitives;

/// <summary>
/// Pins the original connector and owns only the fixed discovery enrollment session resource.
/// </summary>
internal sealed class EventDiscoveryEnrollmentGateCustody : IAsyncDisposable
{
    private const string Resource = "discovery-tenant-enrollment";
    private static readonly ConditionalWeakTable<ExploreDbContext, ContextSlot> Contexts = new();
    private readonly ExploreDbContext _context;
    private readonly ContextSlot _slot;
    private readonly DbContextId _contextId;
    private readonly Guid? _tenantId;
    private readonly string? _providerName;
    private readonly RelationalConnectionIdentity _connection;
    private readonly CancellationTokenSource _abandonment = new();
    private readonly object _lifecycle = new();
    private Task<IAsyncDisposable>? _acquisition;
    private Task? _cleanup;
    private IAsyncDisposable? _sessionLease;
    private bool _openedConnection;
    private bool _initialOpenCompleted;
    private bool _tracksConnection;
    private bool _cancellationDisposed;
    private int? _backendProcessId;
    private int _initialOpenPending;
    private int _connectorChanged;
    private int _connectorReopened;
    private int _acquired;
    private int _disposed;

    private EventDiscoveryEnrollmentGateCustody(ExploreDbContext context, ContextSlot slot)
    {
        _context = context;
        _slot = slot;
        _contextId = context.ContextId;
        _tenantId = context.TenantFilterTenantId;
        _providerName = context.Database.ProviderName;
        _connection = RelationalConnectionIdentity.Capture(context);
    }

    internal static EventDiscoveryEnrollmentGateCustody Create(ExploreDbContext context)
    {
        RequireIdleOwner(context);
        var slot = Contexts.GetValue(context, static _ => new ContextSlot());
        lock (slot)
        {
            if (slot.Custody is not null)
                throw new InvalidOperationException("discovery_enrollment_gate_reentry");
            var custody = new EventDiscoveryEnrollmentGateCustody(context, slot);
            slot.Custody = custody;
            return custody;
        }
    }

    internal Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        lock (_lifecycle)
        {
            if (_acquisition is not null)
                throw new InvalidOperationException("discovery_enrollment_gate_reentry");
            _acquisition = AcquireCoreAsync(cancellationToken);
            return _acquisition;
        }
    }

    internal static void RequireCurrent(ExploreDbContext context)
    {
        if (!Contexts.TryGetValue(context, out var slot))
            throw new InvalidOperationException("discovery_enrollment_gate_missing");
        EventDiscoveryEnrollmentGateCustody custody;
        lock (slot)
            custody = slot.Custody ?? throw new InvalidOperationException("discovery_enrollment_gate_missing");
        if (Volatile.Read(ref custody._disposed) != 0 || Volatile.Read(ref custody._acquired) == 0)
            throw new InvalidOperationException("discovery_enrollment_gate_inactive");
        custody.RequireOriginalConnector();
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
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _abandonment.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            RequireIdleOwner(_context);
            RequireOriginalOwner();
            if (_connection.State is not (ConnectionState.Open or ConnectionState.Closed))
                throw new InvalidOperationException("discovery_enrollment_connector_unavailable");
            bool requiresOpening = _connection.State == ConnectionState.Closed;
            if (requiresOpening)
                Volatile.Write(ref _initialOpenPending, 1);
            _connection.StateChange += OnConnectionStateChange;
            _tracksConnection = true;
            if (requiresOpening)
            {
                _openedConnection = true;
                await _context.Database.OpenConnectionAsync(cancellation.Token).ConfigureAwait(false);
                _initialOpenCompleted = true;
            }
            else
                _backendProcessId = _connection.BackendProcessId;
            RequireOriginalConnector();
            _sessionLease = await RelationalNamedLock.AcquireSessionAsync(
                _context, Resource, cancellation.Token).ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            RequireIdleOwner(_context);
            RequireOriginalConnector();
            Volatile.Write(ref _acquired, 1);
            return this;
        }
        catch
        {
            await CleanupAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static void RequireIdleOwner(ExploreDbContext context)
    {
        context.RequireBoundTenantLease();
        if (!context.Database.IsRelational())
            throw new InvalidOperationException("discovery_enrollment_requires_relational_owner");
        if (Transaction.Current is not null || context.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("discovery_enrollment_requires_pre_begin_owner");
    }

    private void RequireOriginalOwner()
    {
        _context.RequireBoundTenantLease();
        if (_context.ContextId != _contextId || _context.TenantFilterTenantId != _tenantId)
            throw new InvalidOperationException("discovery_enrollment_owner_changed");
    }

    private void RequireOriginalConnector()
    {
        RequireOriginalOwner();
        if (!_connection.Matches(_context) || _connection.State != ConnectionState.Open
            || Volatile.Read(ref _connectorChanged) != 0 || _connection.BackendProcessId != _backendProcessId)
            throw new InvalidOperationException("discovery_enrollment_connector_changed");
    }

    private void OnConnectionStateChange(object? sender, StateChangeEventArgs change)
    {
        if (Volatile.Read(ref _initialOpenPending) != 0)
        {
            if (change.OriginalState == ConnectionState.Closed
                && change.CurrentState == ConnectionState.Connecting)
                return;
            if (change.OriginalState is ConnectionState.Closed or ConnectionState.Connecting
                && change.CurrentState == ConnectionState.Open
                && HasOriginalOwnerAndConnection() && _connection.State == ConnectionState.Open
                && Interlocked.Exchange(ref _initialOpenPending, 0) != 0)
            {
                _backendProcessId = _connection.BackendProcessId;
                return;
            }
            Interlocked.Exchange(ref _initialOpenPending, 0);
            Interlocked.Exchange(ref _connectorChanged, 1);
        }
        if (change.CurrentState != ConnectionState.Open)
            Interlocked.Exchange(ref _connectorChanged, 1);
        else
            Interlocked.Exchange(ref _connectorReopened, 1);
    }

    private bool HasOriginalOwnerAndConnection()
    {
        try
        {
            RequireOriginalOwner();
            return _connection.Matches(_context);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private bool HasOriginalConnector()
    {
        try
        {
            RequireOriginalConnector();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
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
            try
            {
                await acquisition.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Acquisition reports its failure to its caller; disposal still joins its cleanup.
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
        bool lostCustody = false;
        bool releaseAttemptedOnOriginal = false;
        Exception? releaseFailure = null;
        try
        {
            if (_sessionLease is not null)
            {
                bool originalConnector = HasOriginalConnector();
                // SQLite's semaphore belongs to this lease even after its context is re-rented.
                if (_providerName == RelationalNamedLock.SqliteProvider || originalConnector)
                {
                    releaseAttemptedOnOriginal = originalConnector;
                    await _sessionLease.DisposeAsync().ConfigureAwait(false);
                }
                lostCustody = !originalConnector;
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
                // A native release failure may already have closed the original connector.
                bool balanceClosedConnection = (releaseAttemptedOnOriginal || _sessionLease is null)
                    && Volatile.Read(ref _connectorReopened) == 0
                    && HasOriginalOwnerAndConnection() && _connection.State == ConnectionState.Closed;
                bool closeOwnedConnection = _openedConnection
                    && (HasOriginalConnector() || balanceClosedConnection);
                if (_openedConnection && !closeOwnedConnection && _tracksConnection)
                    lostCustody = true;
                if (closeOwnedConnection)
                {
                    try
                    {
                        // A rejected ConnectionOpened interceptor can precede EF's owned-open bookkeeping.
                        if (!_initialOpenCompleted && HasOriginalConnector())
                            await _connection.Connection.CloseAsync().ConfigureAwait(false);
                        if (HasOriginalConnector()
                            || (HasOriginalOwnerAndConnection() && _connection.State == ConnectionState.Closed
                                && Volatile.Read(ref _connectorReopened) == 0))
                            await _context.Database.CloseConnectionAsync().ConfigureAwait(false);
                        else
                            lostCustody = true;
                    }
                    catch (Exception) when (releaseFailure is not null)
                    {
                        // Retain the original session-release failure if balancing EF also fails.
                    }
                }
            }
            finally
            {
                if (_tracksConnection)
                    _connection.StateChange -= OnConnectionStateChange;
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
        if (lostCustody)
            throw new InvalidOperationException("discovery_enrollment_cleanup_lost_custody");
    }

    private sealed class ContextSlot
    {
        internal EventDiscoveryEnrollmentGateCustody? Custody { get; set; }
    }
}
