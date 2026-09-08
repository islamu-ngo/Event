namespace Explore.Application.Contracts.Persistence;

public interface IQueueDrainHealthRepository
{
    Task<QueueDrainHealthSnapshot> GetSnapshotAsync(
        DateTime observedAt,
        DateTime integrationStaleBefore,
        CancellationToken cancellationToken);
}

public sealed record QueueDrainHealthSnapshot(
    int IntegrationDue,
    int IntegrationStale,
    int IntegrationAmbiguous,
    int IncomingDue,
    int IncomingStale,
    int BulkReplayQueued,
    int BulkReplayExecuting,
    int ProviderPublicationDue,
    int ProviderPublicationStale,
    int ProviderPublicationUnknown,
    int PdsDue,
    int PdsStale,
    int PdsDeadLettered);
