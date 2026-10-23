using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class StorageObjectReferenceRepository(ExploreDbContext dbContext)
    : IStorageObjectReferenceRepository, IStorageObjectRetirementEligibilityReader
{
    public Task<IReadOnlyList<StorageObject>> FenceAsync(
        IReadOnlyCollection<Guid> storageObjectIds, CancellationToken cancellationToken) =>
        dbContext.FenceStorageObjectsAsync(storageObjectIds, cancellationToken);

    public async Task<bool> HasBlockingReferencesAsync(Guid storageObjectId, CancellationToken cancellationToken)
    {
        RequireTransaction();
        return await HasBlockingReferencesCoreAsync(storageObjectId, cancellationToken);
    }

    public async Task<bool> CanRetireAsync(
        StorageObject storageObject,
        DateTime serverNowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storageObject);
        if (serverNowUtc == default || serverNowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Retirement evaluation requires a non-default server UTC instant.",
                nameof(serverNowUtc));
        if (storageObject.IsDeleted || storageObject.DeletedAt is not null
            || storageObject.LifecycleState is StorageObjectLifecycleStates.Deleted
                or StorageObjectLifecycleStates.DeleteRequested
            || storageObject.Purpose == StorageObjectPurposes.EventResource
            || storageObject.OwningResourceKind == StorageOwningResourceKinds.EventResource
            || await dbContext.StorageObjectDeletionTombstones.AsNoTracking()
                .AnyAsync(item => item.Id == storageObject.Id, cancellationToken)
            || await HasBlockingReferencesCoreAsync(storageObject.Id, cancellationToken)
            || await HasBlockingHoldsCoreAsync(storageObject.Id, serverNowUtc, cancellationToken))
            return false;

        var sessions = await dbContext.StorageUploadSessions
            .IgnoreQueryFilters([QueryFilterNames.Tenant])
            .AsNoTracking()
            .Where(item => item.StorageObjectId == storageObject.Id || item.Id == storageObject.Id)
            .ToArrayAsync(cancellationToken);
        var operation = await dbContext.Set<StorageProducerOperation>()
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == storageObject.Id, cancellationToken);

        StorageObjectDeletionTombstone target;
        try
        {
            target = StorageRetirementTarget.Capture(storageObject, sessions, operation, serverNowUtc);
        }
        catch (ArgumentException)
        {
            return false;
        }

        return await dbContext.StorageProviderBindings.AsNoTracking()
            .AnyAsync(binding => binding.Id == target.ProviderBindingId
                && binding.Provider == target.Provider, cancellationToken);
    }

    private Task<bool> HasBlockingReferencesCoreAsync(
        Guid storageObjectId,
        CancellationToken cancellationToken) =>
        StorageObjectPhysicalReferenceQuery.HasBlockingReferencesAsync(
            dbContext, storageObjectId, cancellationToken);

    public Task<bool> HasBlockingHoldsAsync(
        Guid storageObjectId, DateTime serverNowUtc, CancellationToken cancellationToken)
    {
        RequireTransaction();
        return HasBlockingHoldsCoreAsync(storageObjectId, serverNowUtc, cancellationToken);
    }

    private async Task<bool> HasBlockingHoldsCoreAsync(
        Guid storageObjectId,
        DateTime serverNowUtc,
        CancellationToken cancellationToken)
    {
        if (serverNowUtc == default || serverNowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Retention evaluation requires a non-default server UTC instant.", nameof(serverNowUtc));

        return await StorageObjectRetentionQuery.HasBlockingHoldsAsync(
            dbContext, storageObjectId, serverNowUtc, cancellationToken);
    }

    private void RequireTransaction()
    {
        if (dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Storage retirement scans require a caller-owned fenced transaction.");
    }
}
