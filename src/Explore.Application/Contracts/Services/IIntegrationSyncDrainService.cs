namespace Explore.Application.Contracts.Services;

public interface IIntegrationSyncDrainService
{
    Task<IntegrationSyncDrainResult> ProcessBatchAsync(CancellationToken cancellationToken);
}

public sealed record IntegrationSyncDrainResult(
    int Pending,
    int Processed,
    int Completed,
    int RetryScheduled,
    int DeadLettered,
    int Ambiguous,
    int AlreadyClaimed);

public sealed record IntegrationSyncSingleDrainResult(
    IntegrationSyncDrainOutcome Outcome,
    Guid OutboxId);

public enum IntegrationSyncDrainOutcome
{
    Completed = 1,
    RetryScheduled = 2,
    DeadLettered = 3,
    Ambiguous = 4,
    AlreadyClaimed = 5
}
