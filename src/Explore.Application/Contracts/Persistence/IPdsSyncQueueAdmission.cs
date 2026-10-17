namespace Explore.Application.Contracts.Persistence;

/// <summary>Atomically admits a bounded global batch without returning private outbox content.</summary>
public interface IPdsSyncQueueAdmission
{
    Task<IReadOnlyList<PdsSyncClaim>> ClaimDueAsync(
        int batchSize,
        string leaseOwner,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);
}
