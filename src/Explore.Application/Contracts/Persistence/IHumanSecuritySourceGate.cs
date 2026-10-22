namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Retains the original selected application's cooperative security-source gate
/// before source observation and through transaction settlement. This is not authorization.
/// </summary>
public interface IHumanSecuritySourceGate
{
    Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default);

    void RequireCurrent();

    /// <summary>
    /// Returns false only when custody is absent. Pending, expired or changed custody throws.
    /// </summary>
    bool TryRequireCurrent();
}
