using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Security.ProviderPrimitives;

namespace Explore.Persistence.Security;

/// <summary>
/// Retains the original application's cooperative security-source gate before BEGIN and
/// through settlement. It is not human authorization or a replacement for SQLite's writer reservation.
/// </summary>
internal sealed class HumanSecuritySourceGate : IHumanSecuritySourceGate, IAsyncDisposable
{
    private readonly ExploreDbContext _context;
    private readonly object _lifecycle = new();
    private HumanSecuritySourceGateCustody? _custody;
    private bool _disposed;

    internal HumanSecuritySourceGate(ExploreDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    internal Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _custody = HumanSecuritySourceGateCustody.Create(_context);
            return _custody.AcquireAsync(cancellationToken);
        }
    }

    internal void RequireCurrent() => HumanSecuritySourceGateCustody.RequireCurrent(_context);

    internal static void RequireCurrent(ExploreDbContext context) =>
        HumanSecuritySourceGateCustody.RequireCurrent(context);

    /// <summary>Only absent custody returns false; a recorded invalid or pending owner remains a refusal.</summary>
    internal static bool TryRequireCurrent(ExploreDbContext context) =>
        HumanSecuritySourceGateCustody.TryRequireCurrent(context);

    Task<IAsyncDisposable> IHumanSecuritySourceGate.AcquireAsync(CancellationToken cancellationToken) =>
        AcquireAsync(cancellationToken);

    void IHumanSecuritySourceGate.RequireCurrent() => RequireCurrent();

    bool IHumanSecuritySourceGate.TryRequireCurrent() => TryRequireCurrent(_context);

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
