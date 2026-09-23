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

    public Task<StorageUploadSession?> GetForAuthorizationAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.StorageUploadSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(session => session.Id == id, cancellationToken);

    public async Task<StorageUploadSession?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        // Provider I/O separates short transactions. Never let the identity map hide cancellation
        // or a competing finalization committed while the provider was running.
        var tracked = _dbContext.StorageUploadSessions.Local.FirstOrDefault(session => session.Id == id);
        if (tracked is not null)
            await _dbContext.Entry(tracked).ReloadAsync(cancellationToken);
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
