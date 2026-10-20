using Explore.Persistence.Security.ProviderPrimitives;

namespace Explore.Persistence.Security;

/// <summary>
/// Retains the original application's cooperative security-source gate before BEGIN and
/// through settlement. It is not human authorization or a replacement for SQLite's writer reservation.
/// </summary>
internal sealed class HumanSecuritySourceGate : IAsyncDisposable
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

    public ValueTask DisposeAsync()
    {
        lock (_lifecycle)
        {
            _disposed = true;
            return _custody?.DisposeAsync() ?? ValueTask.CompletedTask;
        }
    }
}
