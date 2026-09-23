using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

/// <summary>Source-row CAS is the common write fence for activation, settlement and retirement.</summary>
public sealed class EventResourceStorageLifecycleRepository(ExploreDbContext database)
    : IEventResourceStorageLifecycleRepository
{
    public async Task<int> RetireExpiredUploadsAsync(DateTime utcNow, int limit, CancellationToken cancellationToken)
    {
        RequireTransaction();
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        // The scheduler spans tenants, including deleted parents. Every source handoff
        // uses the saved tenant and exact object/session identity, not the request scope.
        var expired = await database.StorageUploadSessions.IgnoreQueryFilters([QueryFilterNames.Tenant]).AsNoTracking()
            .Where(session => session.Purpose == StorageObjectPurposes.EventResource
                && session.OwningResourceKind == StorageOwningResourceKinds.EventResource
                && session.ExpiresAt <= utcNow
                && (session.Status == StorageUploadSessionStates.Reserved || session.Status == StorageUploadSessionStates.Uploading)
                && !database.OrganizationTenantEvidence.IgnoreQueryFilters(new[] { QueryFilterNames.Tenant })
                    .Any(evidence => evidence.TenantId == session.TenantId
                        && evidence.DocumentStorageObjectId == session.StorageObjectId))
            .OrderBy(session => session.ExpiresAt).ThenBy(session => session.Id)
            .Take(limit).ToArrayAsync(cancellationToken);
        int retired = 0;
        foreach (var session in expired)
        {
            if (session.StorageObjectId is { } objectId)
            {
                Guid stamp = Guid.CreateVersion7();
                int changed = await database.StorageUploadSessions.IgnoreQueryFilters([QueryFilterNames.Tenant])
                    .Where(item => item.Id == session.Id && item.TenantId == session.TenantId
                        && item.ConcurrencyStamp == session.ConcurrencyStamp
                        && item.Status == StorageUploadSessionStates.Uploading
                        && item.StorageObjectId == objectId && item.ExpiresAt <= utcNow)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ConcurrencyStamp, stamp),
                        cancellationToken);
                if (changed != 1) throw SourceRemovalConflict();
                DetachSession(session.Id);
                await RetireAsync(session.TenantId, [], [objectId], utcNow, cancellationToken);
                await RemoveTransferredSourcesAsync(session.TenantId, [], [objectId], cancellationToken);
            }
            else
            {
                int changed = await database.StorageUploadSessions.IgnoreQueryFilters([QueryFilterNames.Tenant])
                    .Where(item => item.Id == session.Id && item.TenantId == session.TenantId
                        && item.ConcurrencyStamp == session.ConcurrencyStamp
                        && item.Status == StorageUploadSessionStates.Reserved && item.StorageObjectId == null
                        && item.ObjectKey == null && item.ExpiresAt <= utcNow)
                    .ExecuteDeleteAsync(cancellationToken);
                if (changed != 1) throw SourceRemovalConflict();
                DetachSession(session.Id);
                var counter = await CounterAsync(session.TenantId, session.Provider, cancellationToken);
                counter?.ReleaseReservation(session.ReservedBytes);
                await database.SaveChangesAsync(cancellationToken);
            }
            retired++;
        }
        return retired;
    }

    public async Task RetireAsync(Guid tenantId, IReadOnlyCollection<Guid> resourceIds,
        IReadOnlyCollection<Guid> objectIds, DateTime utcNow, CancellationToken cancellationToken)
    {
        RequireTransaction();
        var sources = await database.StorageObjects
            .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete]).AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.Purpose == StorageObjectPurposes.EventResource
                && item.OwningResourceKind == StorageOwningResourceKinds.EventResource
                && (objectIds.Contains(item.Id) || item.OwningResourceId.HasValue && resourceIds.Contains(item.OwningResourceId.Value)))
            .OrderBy(item => item.Id).ToArrayAsync(cancellationToken);
        var sessions = await database.StorageUploadSessions.IgnoreQueryFilters([QueryFilterNames.Tenant])
            .Where(item => item.TenantId == tenantId && item.Purpose == StorageObjectPurposes.EventResource
                && item.OwningResourceKind == StorageOwningResourceKinds.EventResource
                && (item.StorageObjectId.HasValue && objectIds.Contains(item.StorageObjectId.Value)
                    || item.OwningResourceId.HasValue && resourceIds.Contains(item.OwningResourceId.Value)))
            .ToArrayAsync(cancellationToken);
        foreach (var source in sources)
        {
            // Retention authority is not transferred to ordinary byte cleanup.
            if (await database.OrganizationTenantEvidence.IgnoreQueryFilters([QueryFilterNames.Tenant])
                .AnyAsync(item => item.TenantId == tenantId
                    && item.DocumentStorageObjectId == source.Id, cancellationToken)) continue;
            if (await database.StorageObjectDeletionTombstones.AnyAsync(item => item.Id == source.Id, cancellationToken)) continue;
            if (source.StorageProviderBindingId is not { } bindingId || bindingId == Guid.Empty || source.ObjectKey is null)
                throw new InvalidOperationException("Resource retirement requires its original provider binding.");
            await FenceAsync(source, cancellationToken);
            var producers = sessions.Where(item => item.StorageObjectId == source.Id).ToArray();
            bool settled = producers.Length > 0 && producers.All(item => item.ProducerSettled);
            // Parent redaction may already have saved DeleteRequested. Its still-finalized
            // session is the accounting witness until this handoff closes it atomically.
            bool charged = source.LifecycleState == StorageObjectLifecycleStates.Active
                || producers.Any(item => item.Status == StorageUploadSessionStates.Finalized);
            settled |= source.LifecycleState == StorageObjectLifecycleStates.Active;
            database.StorageObjectDeletionTombstones.Add(StorageObjectDeletionTombstone.Create(source.Id,
                tenantId, source.Provider, bindingId, source.ObjectKey, source.ProviderVersionId, settled, utcNow));
            if (charged)
            {
                var counter = await CounterAsync(tenantId, source.Provider, cancellationToken);
                if (counter is not null)
                {
                    counter.UsedBytes = Math.Max(0, counter.UsedBytes - source.Size);
                    counter.ObjectCount = Math.Max(0, counter.ObjectCount - 1);
                }
            }
            source.RequestDelete();
            database.StorageObjects.Update(source);
        }
        foreach (var session in sessions)
        {
            if (session.StorageObjectId is { } objectId
                && await database.OrganizationTenantEvidence.IgnoreQueryFilters([QueryFilterNames.Tenant])
                    .AnyAsync(item => item.TenantId == tenantId
                        && item.DocumentStorageObjectId == objectId, cancellationToken)) continue;
            if (session.Status is StorageUploadSessionStates.Reserved or StorageUploadSessionStates.Uploading)
            {
                var counter = await CounterAsync(tenantId, session.Provider, cancellationToken);
                counter?.ReleaseReservation(session.ReservedBytes);
            }
            if (session.Status is StorageUploadSessionStates.Reserved or StorageUploadSessionStates.Uploading or StorageUploadSessionStates.Finalized)
                session.Fail("resource_storage_retired", null, utcNow);
        }
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveTransferredSourcesAsync(Guid tenantId, IReadOnlyCollection<Guid> resourceIds,
        IReadOnlyCollection<Guid> objectIds, CancellationToken cancellationToken)
    {
        RequireTransaction();
        var sources = await database.StorageObjects
            .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete]).AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.Purpose == StorageObjectPurposes.EventResource
                && item.OwningResourceKind == StorageOwningResourceKinds.EventResource
                && (objectIds.Contains(item.Id) || item.OwningResourceId.HasValue && resourceIds.Contains(item.OwningResourceId.Value))
                && !database.OrganizationTenantEvidence.IgnoreQueryFilters(new[] { QueryFilterNames.Tenant }).Any(evidence =>
                    evidence.TenantId == item.TenantId && evidence.DocumentStorageObjectId == item.Id))
            .OrderBy(item => item.Id).ToArrayAsync(cancellationToken);
        var sessions = await database.StorageUploadSessions.IgnoreQueryFilters([QueryFilterNames.Tenant]).AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.Purpose == StorageObjectPurposes.EventResource
                && item.OwningResourceKind == StorageOwningResourceKinds.EventResource
                && (item.StorageObjectId.HasValue && objectIds.Contains(item.StorageObjectId.Value)
                    || item.OwningResourceId.HasValue && resourceIds.Contains(item.OwningResourceId.Value))
                && !database.OrganizationTenantEvidence.IgnoreQueryFilters(new[] { QueryFilterNames.Tenant }).Any(evidence =>
                    evidence.TenantId == item.TenantId && evidence.DocumentStorageObjectId == item.StorageObjectId))
            .ToArrayAsync(cancellationToken);
        var ids = sources.Select(item => item.Id).Concat(sessions.Where(item => item.StorageObjectId.HasValue)
            .Select(item => item.StorageObjectId!.Value)).Distinct().ToArray();
        var tombstones = await database.StorageObjectDeletionTombstones.AsNoTracking()
            .Where(item => item.TenantId == tenantId && ids.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        foreach (var source in sources)
        {
            if (source.LifecycleState != StorageObjectLifecycleStates.DeleteRequested
                || !tombstones.TryGetValue(source.Id, out var tombstone)
                || tombstone.Provider != source.Provider || tombstone.ProviderBindingId != source.StorageProviderBindingId
                || tombstone.ObjectKey != source.ObjectKey
                || source.ProviderVersionId is not null && tombstone.ProviderObjectVersion != source.ProviderVersionId
                || await database.EventResources
                    .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
                    .AnyAsync(item => item.TenantId == tenantId
                        && item.StorageObjectId == source.Id, cancellationToken)
                || await database.OrganizationTenantEvidence.IgnoreQueryFilters([QueryFilterNames.Tenant])
                    .AnyAsync(item => item.TenantId == tenantId
                        && item.DocumentStorageObjectId == source.Id, cancellationToken))
                throw new InvalidOperationException("Source removal requires detached, non-evidence storage with transferred deletion authority.");
        }
        foreach (var session in sessions)
        {
            if (session.Status is StorageUploadSessionStates.Reserved or StorageUploadSessionStates.Uploading or StorageUploadSessionStates.Finalized
                || session.StorageObjectId is null && session.ObjectKey is not null
                || session.StorageObjectId is { } objectId
                    && (!tombstones.TryGetValue(objectId, out var tombstone) || tombstone.Provider != session.Provider
                        || tombstone.ProviderBindingId != session.StorageProviderBindingId || tombstone.ObjectKey != session.ObjectKey
                        || session.ProducerSettled && tombstone.ProviderObjectVersion != session.ProviderVersionId))
                throw new InvalidOperationException("Source session removal requires closed accounting and transferred deletion authority.");
        }
        foreach (var session in sessions)
        {
            int changed = await database.StorageUploadSessions.IgnoreQueryFilters([QueryFilterNames.Tenant])
                .Where(item => item.Id == session.Id && item.TenantId == tenantId
                    && item.ConcurrencyStamp == session.ConcurrencyStamp)
                .ExecuteDeleteAsync(cancellationToken);
            if (changed != 1) throw SourceRemovalConflict();
            DetachSession(session.Id);
        }
        foreach (var source in sources)
        {
            int changed = await database.StorageObjects
                .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
                .Where(item => item.Id == source.Id && item.TenantId == tenantId
                    && item.ConcurrencyStamp == source.ConcurrencyStamp
                    && database.StorageObjectDeletionTombstones.Any(tombstone => tombstone.Id == item.Id)
                    && !database.EventResources
                        .IgnoreQueryFilters(new[] { QueryFilterNames.Tenant, QueryFilterNames.SoftDelete })
                        .Any(resource => resource.TenantId == tenantId
                            && resource.StorageObjectId == item.Id))
                .ExecuteDeleteAsync(cancellationToken);
            if (changed != 1) throw SourceRemovalConflict();
            DetachObject(source.Id);
        }
    }

    private static ConcurrencyConflictException SourceRemovalConflict() => new(
        ConcurrencyConflictException.ConcurrentUpdate, "Resource source handoff changed concurrently.");

    public async Task RecordProducerSettlementAsync(EventResourceProducerIdentity identity, string? providerVersion,
        DateTime utcNow, CancellationToken cancellationToken)
    {
        RequireTransaction();
        var tombstones = new StorageObjectDeletionTombstoneRepository(database);
        var tombstone = await tombstones.GetByIdAsync(identity.ObjectId, cancellationToken);
        if (tombstone is not null
            && (tombstone.TenantId != identity.TenantId || tombstone.Provider != identity.Provider
                || !await tombstones.TrySettleProducerAsync(identity.ObjectId, identity.BindingId,
                    identity.ObjectKey, providerVersion, utcNow, cancellationToken))) return;
        // Keep any surviving closed source identity in sync, without reopening it. If erasure
        // removed those rows, the independent tombstone above is the entire acknowledgement.
        var session = await database.StorageUploadSessions.IgnoreQueryFilters([QueryFilterNames.Tenant]).AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == identity.SessionId && item.TenantId == identity.TenantId, cancellationToken);
        var source = await database.StorageObjects
            .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete]).AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == identity.ObjectId && item.TenantId == identity.TenantId, cancellationToken);
        if (session is null || source is null || session.StorageObjectId != identity.ObjectId
            || session.Provider != identity.Provider || session.StorageProviderBindingId != identity.BindingId
            || session.ObjectKey != identity.ObjectKey || source.Provider != identity.Provider
            || source.StorageProviderBindingId != identity.BindingId || source.ObjectKey != identity.ObjectKey
            || session.ProducerSettled) return;
        await FenceAsync(source, cancellationToken);
        DetachSession(session.Id);
        session.RecordProducerSettlement(identity.ObjectId, identity.BindingId, identity.ObjectKey, providerVersion);
        source.ProviderVersionId = providerVersion;
        database.StorageUploadSessions.Update(session);
        database.StorageObjects.Update(source);
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<StorageObject?> FenceActivationAsync(StorageUploadSession session, CancellationToken cancellationToken)
    {
        RequireTransaction();
        if (!session.ProducerSettled || session.Status != StorageUploadSessionStates.Uploading
            || session.StorageProviderBindingId is not { } bindingId || bindingId == Guid.Empty) return null;
        var source = await database.StorageObjects
            .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete]).AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == session.StorageObjectId && item.TenantId == session.TenantId
                && !item.IsDeleted && item.LifecycleState == StorageObjectLifecycleStates.DeleteRequested
                && item.Provider == session.Provider && item.StorageProviderBindingId == bindingId
                && item.ProviderVersionId == session.ProviderVersionId && item.ObjectKey == session.ObjectKey
                && item.Size == session.ExpectedSizeBytes && item.ContentType == session.ContentType
                && item.OwningResourceId == session.OwningResourceId
                && item.OwningResourceKind == StorageOwningResourceKinds.EventResource
                && item.Purpose == StorageObjectPurposes.EventResource && item.Visibility == StorageObjectVisibilities.PrivateOwner,
                cancellationToken);
        if (source is null) return null;
        Guid stamp = Guid.CreateVersion7();
        int changed = await database.StorageObjects
            .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
            .Where(item => item.Id == source.Id && item.TenantId == session.TenantId
                && item.ConcurrencyStamp == source.ConcurrencyStamp
                && !database.StorageObjectDeletionTombstones.Any(tombstone => tombstone.Id == item.Id)
                && database.StorageUploadSessions.IgnoreQueryFilters(new[] { QueryFilterNames.Tenant })
                    .Any(producer => producer.Id == session.Id && producer.TenantId == session.TenantId
                    && producer.ConcurrencyStamp == session.ConcurrencyStamp && producer.ProducerSettled
                    && producer.Status == StorageUploadSessionStates.Uploading))
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ConcurrencyStamp, stamp), cancellationToken);
        if (changed != 1) return null;
        DetachObject(source.Id);
        source.ConcurrencyStamp = stamp;
        return source;
    }

    private async Task FenceAsync(StorageObject source, CancellationToken cancellationToken)
    {
        Guid stamp = Guid.CreateVersion7();
        int changed = await database.StorageObjects
            .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
            .Where(item => item.Id == source.Id && item.TenantId == source.TenantId
                && item.ConcurrencyStamp == source.ConcurrencyStamp)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ConcurrencyStamp, stamp), cancellationToken);
        if (changed != 1) throw new ConcurrencyConflictException(ConcurrencyConflictException.ConcurrentUpdate,
            "Resource storage lifecycle changed concurrently.");
        DetachObject(source.Id);
        source.ConcurrencyStamp = stamp;
    }

    private Task<StorageUsageCounter?> CounterAsync(Guid tenantId, string provider, CancellationToken cancellationToken) =>
        database.StorageUsageCounters.IgnoreQueryFilters([QueryFilterNames.Tenant])
            .SingleOrDefaultAsync(item => item.TenantId == tenantId && item.Provider == provider, cancellationToken);

    private void DetachObject(Guid id)
    {
        foreach (var entry in database.ChangeTracker.Entries<StorageObject>().Where(item => item.Entity.Id == id).ToArray())
            entry.State = EntityState.Detached;
    }

    private void DetachSession(Guid id)
    {
        foreach (var entry in database.ChangeTracker.Entries<StorageUploadSession>().Where(item => item.Entity.Id == id).ToArray())
            entry.State = EntityState.Detached;
    }

    private void RequireTransaction()
    {
        if (database.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Resource lifecycle changes require the native caller's transaction.");
    }
}
