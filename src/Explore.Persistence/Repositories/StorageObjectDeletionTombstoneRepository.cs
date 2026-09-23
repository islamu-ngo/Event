using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class StorageObjectDeletionTombstoneRepository(ExploreDbContext database)
    : IStorageObjectDeletionTombstoneRepository
{
    public Task<StorageObjectDeletionTombstone?> GetByIdAsync(Guid objectId, CancellationToken cancellationToken) =>
        database.StorageObjectDeletionTombstones.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == objectId, cancellationToken);

    public async Task AddAsync(StorageObjectDeletionTombstone tombstone, CancellationToken cancellationToken) =>
        await database.StorageObjectDeletionTombstones.AddAsync(tombstone, cancellationToken);

    public async Task<IReadOnlyList<StorageObjectDeletionTombstone>> ListWithSourcesAsync(
        int limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        return await database.StorageObjectDeletionTombstones.AsNoTracking()
            .Where(item => database.StorageObjects
                    .IgnoreQueryFilters(new[] { QueryFilterNames.Tenant, QueryFilterNames.SoftDelete })
                    .Any(source => source.Id == item.Id && source.TenantId == item.TenantId)
                || database.StorageUploadSessions.IgnoreQueryFilters(new[] { QueryFilterNames.Tenant })
                    .Any(session => session.StorageObjectId == item.Id && session.TenantId == item.TenantId))
            .OrderBy(item => item.Id).Take(limit).ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StorageObjectDeletionTombstone>> ListDueAsync(
        DateTime utcNow, int limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        return await database.StorageObjectDeletionTombstones.AsNoTracking()
            .Where(item => item.State == StorageObjectDeletionState.Ready && item.NextAttemptAtUtc <= utcNow
                || item.State == StorageObjectDeletionState.Deleting && item.LeaseExpiresAtUtc <= utcNow)
            .OrderBy(item => item.NextAttemptAtUtc ?? item.LeaseExpiresAtUtc)
            .ThenBy(item => item.Id).Take(limit).ToArrayAsync(cancellationToken);
    }

    public async Task<StorageObjectDeletionTombstone?> TryClaimAsync(
        Guid objectId, Guid expectedStamp, DateTime utcNow, DateTime leaseExpiresAtUtc, CancellationToken cancellationToken)
    {
        var work = await GetByIdAsync(objectId, cancellationToken);
        if (work is null || !work.TryClaim(expectedStamp, Guid.CreateVersion7(), utcNow, leaseExpiresAtUtc))
            return null;
        int changed = await database.StorageObjectDeletionTombstones
            .Where(item => item.Id == objectId && item.ConcurrencyStamp == expectedStamp
                && !database.StorageObjects
                    .IgnoreQueryFilters(new[] { QueryFilterNames.Tenant, QueryFilterNames.SoftDelete })
                    .Any(source => source.Id == item.Id && source.TenantId == item.TenantId)
                && !database.StorageUploadSessions.IgnoreQueryFilters(new[] { QueryFilterNames.Tenant })
                    .Any(session => session.StorageObjectId == item.Id && session.TenantId == item.TenantId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.State, work.State)
                .SetProperty(item => item.ConcurrencyStamp, work.ConcurrencyStamp)
                .SetProperty(item => item.NextAttemptAtUtc, work.NextAttemptAtUtc)
                .SetProperty(item => item.LeaseExpiresAtUtc, work.LeaseExpiresAtUtc), cancellationToken);
        return changed == 1 ? work : null;
    }

    public async Task<bool> TrySettleProducerAsync(Guid objectId, Guid bindingId, string objectKey,
        string? providerVersion, DateTime utcNow, CancellationToken cancellationToken)
    {
        var work = await GetByIdAsync(objectId, cancellationToken);
        if (work is null || work.ProviderBindingId != bindingId
            || !string.Equals(work.ObjectKey, objectKey, StringComparison.Ordinal))
            return false;
        Guid expectedStamp = work.ConcurrencyStamp;
        if (!work.TrySettleProducer(providerVersion, utcNow)) return false;
        return await database.StorageObjectDeletionTombstones
            .Where(item => item.Id == objectId && item.ConcurrencyStamp == expectedStamp
                && item.State == StorageObjectDeletionState.AwaitingProducer)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.State, work.State)
                .SetProperty(item => item.ConcurrencyStamp, work.ConcurrencyStamp)
                .SetProperty(item => item.ProviderObjectVersion, work.ProviderObjectVersion)
                .SetProperty(item => item.NextAttemptAtUtc, work.NextAttemptAtUtc), cancellationToken) == 1;
    }

    public async Task<bool> TryRecordAbsenceAsync(
        Guid objectId, Guid claimStamp, DateTime utcNow, CancellationToken cancellationToken)
    {
        var work = await GetByIdAsync(objectId, cancellationToken);
        if (work is null || !work.TryRecordAbsence(claimStamp, utcNow)) return false;
        // Absence is the terminal transition; there is no retention-based purge.
        return await database.StorageObjectDeletionTombstones
            .Where(item => item.Id == objectId && item.State == StorageObjectDeletionState.Deleting
                && item.ConcurrencyStamp == claimStamp && item.LeaseExpiresAtUtc > utcNow
                && !database.StorageObjects
                    .IgnoreQueryFilters(new[] { QueryFilterNames.Tenant, QueryFilterNames.SoftDelete })
                    .Any(source => source.Id == item.Id && source.TenantId == item.TenantId)
                && !database.StorageUploadSessions.IgnoreQueryFilters(new[] { QueryFilterNames.Tenant })
                    .Any(session => session.StorageObjectId == item.Id && session.TenantId == item.TenantId))
            .ExecuteDeleteAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryScheduleRetryAsync(Guid objectId, Guid claimStamp, DateTime utcNow,
        DateTime nextAttemptAtUtc, CancellationToken cancellationToken)
    {
        var work = await GetByIdAsync(objectId, cancellationToken);
        if (work is null || !work.TryScheduleRetry(claimStamp, utcNow, nextAttemptAtUtc)) return false;
        return await database.StorageObjectDeletionTombstones
            .Where(item => item.Id == objectId && item.State == StorageObjectDeletionState.Deleting
                && item.ConcurrencyStamp == claimStamp && item.LeaseExpiresAtUtc > utcNow)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.State, work.State)
                .SetProperty(item => item.ConcurrencyStamp, work.ConcurrencyStamp)
                .SetProperty(item => item.NextAttemptAtUtc, work.NextAttemptAtUtc)
                .SetProperty(item => item.LeaseExpiresAtUtc, work.LeaseExpiresAtUtc), cancellationToken) == 1;
    }
}
