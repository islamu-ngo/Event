namespace Explore.Application.Contracts.Services;

public interface IPdsSyncDrainService
{
    Task<PdsSyncDrainResult> ProcessBatchAsync(CancellationToken cancellationToken);
}

public sealed record PdsSyncDrainResult(
    int ClaimedCount,
    int DeliveredCount,
    int FailedCount,
    int ClaimLostCount);
