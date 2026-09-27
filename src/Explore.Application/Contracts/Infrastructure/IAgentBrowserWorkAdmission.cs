namespace Explore.Application.Contracts.Infrastructure;

/// <summary>
/// Development-only admission of complete background work units. The API's agent reset owner closes
/// admission and awaits every acquired lease before purging; ordinary hosts do not register this port.
/// Acquire before reading a batch or claim and retain the lease through its final durable write.
/// </summary>
public interface IAgentBrowserWorkAdmission
{
    ValueTask<IDisposable> EnterAsync(CancellationToken cancellationToken);

    /// <summary>Ends idle waits in long-lived subscriptions; their acquired lease must still drain.</summary>
    CancellationToken MaintenanceRequested { get; }
}
