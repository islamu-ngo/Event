using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class IdempotencyRepository : IIdempotencyRepository
{
    private const int MaximumInsertAttempts = 3;
    private readonly ExploreDbContext _dbContext;

    public IdempotencyRepository(ExploreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IdempotencyRecord?> FindAsync(
        string key,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(
                record => record.Key == key && record.TenantId == tenantId,
                cancellationToken);
    }

    public async Task SaveAsync(
        IdempotencyRecord record,
        CancellationToken cancellationToken = default)
    {
        _dbContext.IdempotencyRecords.Add(record);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IdempotencyClaim> TryClaimAsync(
        IdempotencyRecord record,
        CancellationToken cancellationToken = default)
    {
        if (!_dbContext.Database.IsRelational())
        {
            IdempotencyRecord? current = await FindAsync(
                record.Key,
                record.TenantId,
                cancellationToken);
            if (current is null)
            {
                await SaveAsync(record, cancellationToken);
                return new IdempotencyClaim(record, IsOwner: true);
            }

            return new IdempotencyClaim(current, IsOwner: false);
        }

        DbUpdateException? lastConflict = null;
        for (int attempt = 0; attempt < MaximumInsertAttempts; attempt++)
        {
            IdempotencyRecord? current = await FindAsync(
                record.Key,
                record.TenantId,
                cancellationToken);
            if (current is not null)
            {
                if (current.ExpiresAt > record.CreatedAt)
                {
                    return new IdempotencyClaim(current, IsOwner: false);
                }

                await _dbContext.IdempotencyRecords
                    .Where(candidate =>
                        candidate.Key == record.Key &&
                        candidate.TenantId == record.TenantId &&
                        candidate.ExpiresAt <= record.CreatedAt)
                    .ExecuteDeleteAsync(cancellationToken);
            }

            _dbContext.IdempotencyRecords.Add(record);
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return new IdempotencyClaim(record, IsOwner: true);
            }
            catch (DbUpdateException exception)
                when (RegistrationUniqueConflictClassifier.IsProviderUniqueConflict(exception))
            {
                lastConflict = exception;
                _dbContext.Entry(record).State = EntityState.Detached;
            }
        }

        IdempotencyRecord? winner = await FindAsync(
            record.Key,
            record.TenantId,
            cancellationToken);
        if (winner is not null)
        {
            return new IdempotencyClaim(winner, IsOwner: false);
        }

        throw new InvalidOperationException(
            "The idempotency claim race did not produce a durable owner.",
            lastConflict);
    }

    public async Task<bool> CompleteAsync(
        Guid recordId,
        int statusCode,
        string? responseBody,
        string? contentType,
        CancellationToken cancellationToken = default)
    {
        if (!_dbContext.Database.IsRelational())
        {
            IdempotencyRecord? record = await _dbContext.IdempotencyRecords.FirstOrDefaultAsync(
                candidate => candidate.Id == recordId
                    && candidate.StatusCode == IdempotencyRecord.InProgressStatusCode,
                cancellationToken);
            if (record is null)
            {
                return false;
            }

            record.StatusCode = statusCode;
            record.ResponseBody = responseBody;
            record.ContentType = contentType;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        int updated = await _dbContext.IdempotencyRecords
            .Where(record =>
                record.Id == recordId &&
                record.StatusCode == IdempotencyRecord.InProgressStatusCode)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(record => record.StatusCode, statusCode)
                    .SetProperty(record => record.ResponseBody, responseBody)
                    .SetProperty(record => record.ContentType, contentType),
                cancellationToken);
        return updated == 1;
    }

    public async Task<bool> ReleaseAsync(
        Guid recordId,
        CancellationToken cancellationToken = default)
    {
        if (!_dbContext.Database.IsRelational())
        {
            IdempotencyRecord? record = await _dbContext.IdempotencyRecords.FirstOrDefaultAsync(
                candidate => candidate.Id == recordId
                    && candidate.StatusCode == IdempotencyRecord.InProgressStatusCode,
                cancellationToken);
            if (record is null)
            {
                return false;
            }

            _dbContext.IdempotencyRecords.Remove(record);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        int deleted = await _dbContext.IdempotencyRecords
            .Where(record =>
                record.Id == recordId &&
                record.StatusCode == IdempotencyRecord.InProgressStatusCode)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted == 1;
    }

    public async Task<int> CountExpiredAsync(
        DateTime expiresBeforeUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.IdempotencyRecords
            .AsNoTracking()
            .Where(record => record.ExpiresAt <= expiresBeforeUtc)
            .OrderBy(record => record.ExpiresAt)
            .Take(batchSize)
            .CountAsync(cancellationToken);
    }

    public async Task<int> DeleteExpiredAsync(
        DateTime expiresBeforeUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Guid> expiredRecordIds = _dbContext.IdempotencyRecords
            .Where(record => record.ExpiresAt <= expiresBeforeUtc)
            .OrderBy(record => record.ExpiresAt)
            .Select(record => record.Id)
            .Take(batchSize);

        return await _dbContext.IdempotencyRecords
            .Where(record => expiredRecordIds.Contains(record.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
