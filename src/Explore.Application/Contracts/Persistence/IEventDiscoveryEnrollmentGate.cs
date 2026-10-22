namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Pins the original selected owner and serializes discovery enrollment before its transaction starts.
/// </summary>
public interface IEventDiscoveryEnrollmentGate
{
    Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default);

    /// <summary>Validates an already active lease belonging to this original owner.</summary>
    void RequireCurrent();

    /// <summary>Returns false only for absent custody; pending, released or changed custody throws.</summary>
    bool TryRequireCurrent();
}
