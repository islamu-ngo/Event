using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IStorageUploadSessionRepository : IGenericRepository<StorageUploadSession, Guid>
{
    Task<StorageUploadSession?> GetActiveByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<StorageUploadSession?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task<StorageUploadSession?> GetByTenantAndIdempotencyKeyForUpdateAsync(
        Guid tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<StorageUploadSession>> ListExpiredReservationsAsync(DateTime utcNow, int limit, CancellationToken cancellationToken);
}
