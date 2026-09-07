namespace Explore.ServiceDefaults.HealthChecks;

public interface IDistributedCacheBackendState
{
    string BackendName { get; }

    bool IsConfigured { get; }

    bool IsDegraded { get; }

    string Status { get; }
}
