using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class StorageUploadSessionRepository : GenericRepository<StorageUploadSession, Guid>, IStorageUploadSessionRepository
{
    private readonly ExploreDbContext _dbContext;

    public StorageUploadSessionRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<StorageUploadSession?> GetActiveByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.StorageUploadSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(session =>
                    session.Id == id &&
                    session.Status != StorageUploadSessionStates.Finalized &&
                    session.Status != StorageUploadSessionStates.Canceled &&
                    session.Status != StorageUploadSessionStates.Failed &&
                    session.Status != StorageUploadSessionStates.Expired,
                cancellationToken);
    }

    public async Task<StorageUploadSession?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.StorageUploadSessions
            .FirstOrDefaultAsync(session => session.Id == id, cancellationToken);
    }

    public async Task<StorageUploadSession?> GetByTenantAndIdempotencyKeyForUpdateAsync(
        Guid tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return await _dbContext.StorageUploadSessions
            .FirstOrDefaultAsync(session =>
                    session.TenantId == tenantId &&
                    session.IdempotencyKey == idempotencyKey,
                cancellationToken);
    }

    public async Task<IReadOnlyList<StorageUploadSession>> ListExpiredReservationsAsync(
        DateTime utcNow,
        int limit,
        CancellationToken cancellationToken)
    {
        return await _dbContext.StorageUploadSessions
            .AsNoTracking()
            .Where(session =>
                session.ExpiresAt <= utcNow &&
                (session.Status == StorageUploadSessionStates.Reserved ||
                 session.Status == StorageUploadSessionStates.Uploading))
            .OrderBy(session => session.ExpiresAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
