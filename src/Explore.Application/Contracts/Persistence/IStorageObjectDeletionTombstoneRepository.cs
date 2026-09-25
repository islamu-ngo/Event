using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IStorageObjectDeletionTombstoneRepository
{
    Task<StorageObjectDeletionTombstone?> GetByIdAsync(Guid objectId, CancellationToken cancellationToken);
    Task AddAsync(StorageObjectDeletionTombstone tombstone, CancellationToken cancellationToken);
    Task<IReadOnlyList<StorageObjectDeletionTombstone>> ListWithSourcesAsync(
        int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<StorageObjectDeletionTombstone>> ListDueAsync(
        DateTime utcNow, int limit, CancellationToken cancellationToken);
    Task<StorageObjectDeletionTombstone?> TryClaimAsync(
        Guid objectId, Guid expectedStamp, DateTime utcNow, DateTime leaseExpiresAtUtc, CancellationToken cancellationToken);
    Task<bool> TrySettleProducerAsync(Guid objectId, Guid bindingId, string objectKey, string? providerVersion,
        DateTime utcNow, CancellationToken cancellationToken);
    Task<bool> TryRecordAbsenceAsync(Guid objectId, Guid claimStamp, DateTime utcNow, CancellationToken cancellationToken);
    Task<bool> TryScheduleRetryAsync(Guid objectId, Guid claimStamp, DateTime utcNow,
        DateTime nextAttemptAtUtc, CancellationToken cancellationToken);
}
