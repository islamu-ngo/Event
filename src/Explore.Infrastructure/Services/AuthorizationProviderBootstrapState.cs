using Explore.Application.Authorization;

namespace Explore.Infrastructure.Services;

public sealed class AuthorizationProviderBootstrapState
{
    public const string NotApplicable = "not-applicable";
    public const string Pending = "pending";
    public const string Ready = "ready";
    public const string Failed = "failed";

    private readonly object _sync = new();
    private Lazy<Task<AuthorizationProviderReconciliationResult>>? _inFlight;
    private AuthorizationProviderBootstrapSnapshot _snapshot = new(
        Provider: null,
        Status: NotApplicable,
        EndpointVerified: false,
        PoliciesSynchronized: false,
        Message: null);

    public AuthorizationProviderBootstrapSnapshot Read()
    {
        lock (_sync)
        {
            return _snapshot;
        }
    }

    public Task<AuthorizationProviderReconciliationResult> RunSingleFlightAsync(
        Func<CancellationToken, Task<AuthorizationProviderReconciliationResult>> operation,
        CancellationToken cancellationToken)
    {
        Lazy<Task<AuthorizationProviderReconciliationResult>> flight;
        bool isOwner;

        lock (_sync)
        {
            isOwner = _inFlight is null;
            flight = _inFlight ??= new(() => operation(cancellationToken));
        }

        var task = flight.Value;
        return isOwner
            ? CompleteOwnerAsync(flight, task)
            : task.WaitAsync(cancellationToken);
    }

    public void MarkPending(string provider)
    {
        Set(new(provider, Pending, false, false, null));
    }

    public void MarkPendingAfterFailure()
    {
        lock (_sync)
        {
            if (_snapshot.Status == Failed && !string.IsNullOrWhiteSpace(_snapshot.Provider))
            {
                _snapshot = new(_snapshot.Provider, Pending, false, false, null);
            }
        }
    }

    public void MarkReady(string provider, bool endpointVerified, bool policiesSynchronized, string message)
    {
        Set(new(provider, Ready, endpointVerified, policiesSynchronized, message));
    }

    public void MarkFailed(string provider, bool endpointVerified, string message)
    {
        Set(new(provider, Failed, endpointVerified, false, message));
    }

    private void Set(AuthorizationProviderBootstrapSnapshot snapshot)
    {
        lock (_sync)
        {
            _snapshot = snapshot;
        }
    }

    private async Task<AuthorizationProviderReconciliationResult> CompleteOwnerAsync(
        Lazy<Task<AuthorizationProviderReconciliationResult>> flight,
        Task<AuthorizationProviderReconciliationResult> task)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_inFlight, flight))
                {
                    _inFlight = null;
                }
            }
        }
    }
}

public sealed record AuthorizationProviderBootstrapSnapshot(
    string? Provider,
    string Status,
    bool EndpointVerified,
    bool PoliciesSynchronized,
    string? Message);
