using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database.ProviderPrimitives;

namespace Explore.Persistence.Services;

/// <summary>
/// Owns the original context's discovery enrollment lease across transaction settlement.
/// </summary>
public sealed class EventDiscoveryEnrollmentGate : IEventDiscoveryEnrollmentGate, IAsyncDisposable
{
    private readonly ExploreDbContext _context;
    private readonly object _lifecycle = new();
    private EventDiscoveryEnrollmentGateCustody? _custody;
    private bool _disposed;

    public EventDiscoveryEnrollmentGate(ExploreDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _custody = EventDiscoveryEnrollmentGateCustody.Create(_context);
            return _custody.AcquireAsync(cancellationToken);
        }
    }

    public void RequireCurrent() => EventDiscoveryEnrollmentGateCustody.RequireCurrent(_context);

    public bool TryRequireCurrent() => EventDiscoveryEnrollmentGateCustody.TryRequireCurrent(_context);

    internal static void RequireCurrent(ExploreDbContext context) =>
        EventDiscoveryEnrollmentGateCustody.RequireCurrent(context);

    /// <summary>
    /// Returns false only when no custody is recorded; invalid recorded custody still throws.
    /// </summary>
    internal static bool TryRequireCurrent(ExploreDbContext context) =>
        EventDiscoveryEnrollmentGateCustody.TryRequireCurrent(context);

    public ValueTask DisposeAsync()
    {
        lock (_lifecycle)
        {
            _disposed = true;
            // The returned lease/acquisition retains its first cleanup result.
            // A later DI disposal must not replace the caller's composed failure.
            if (_custody is null || _custody.HasCompletedCleanup)
                return ValueTask.CompletedTask;
            return _custody.DisposeAsync();
        }
    }
}
