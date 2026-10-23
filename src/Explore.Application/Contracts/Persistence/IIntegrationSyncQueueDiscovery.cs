namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Discovers bounded queue ownership without granting access to subscriber payloads or claims.
/// Each candidate must be reloaded under its exact tenant before recovery or execution.
/// </summary>
public interface IIntegrationSyncQueueDiscovery
{
    const int MaximumBatchSize = 1000;

    Task<IReadOnlyList<IntegrationSyncQueueCandidate>> GetPendingAsync(
        int batchSize,
        DateTime now,
        DateTime staleProcessingStartedBefore,
        CancellationToken cancellationToken);
}

public sealed record IntegrationSyncQueueCandidate(Guid TenantId, Guid OutboxId);
