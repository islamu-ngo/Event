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
    public async Task<StorageRetirementAdmission> TryQueueRetirementAsync(
        Guid tenantId, Guid storageObjectId, DateTime utcNow, CancellationToken cancellationToken)
    {
        RequireTransaction();
        RequireUtc(utcNow);
        try
        {
            var source = await SourceAsync(tenantId, storageObjectId, cancellationToken);
            var existing = await database.StorageObjectDeletionTombstones.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == storageObjectId && item.TenantId == tenantId, cancellationToken);
            if (source is null)
            {
                if (existing is null) return StorageRetirementAdmission.NotFound;
                var survivingSessions = await SessionsAsync(storageObjectId, cancellationToken);
                var survivingOperation = await OperationAsync(storageObjectId, cancellationToken);
                if (survivingSessions.Any(session => session.TenantId != tenantId
                    || session.Provider != existing.Provider || session.StorageProviderBindingId != existing.ProviderBindingId
                    || session.ObjectKey != existing.ObjectKey
                    || session.ProviderVersionId is not null && session.ProviderVersionId != existing.ProviderObjectVersion)
                    || survivingOperation is not null && (survivingOperation.TenantId != tenantId
                        || survivingOperation.Provider != existing.Provider
                        || survivingOperation.ProviderBindingId != existing.ProviderBindingId
                        || survivingOperation.ObjectKey != existing.ObjectKey
                        || survivingOperation.ProviderVersionId is not null
                            && survivingOperation.ProviderVersionId != existing.ProviderObjectVersion))
                    return StorageRetirementAdmission.InvalidTarget;
                return StorageRetirementAdmission.Pending;
            }

            var references = new StorageObjectReferenceRepository(database);
            await references.FenceAsync([source.Id], cancellationToken);
            // A staged detachment is not proof: inspect the physical owners only
            // after the caller's tracked mutations have actually reached the database.
            await database.SaveChangesAsync(cancellationToken);
            source = await SourceAsync(tenantId, storageObjectId, cancellationToken)
                ?? throw SourceRemovalConflict();
            if (await references.HasBlockingReferencesAsync(source.Id, cancellationToken))
                return StorageRetirementAdmission.InUse;
            if (await references.HasBlockingHoldsAsync(source.Id, utcNow, cancellationToken))
                return StorageRetirementAdmission.RetentionBlocked;
            var sessions = await SessionsAsync(source.Id, cancellationToken);
            var operation = await OperationAsync(source.Id, cancellationToken);
            if (!await TransferAsync(source, sessions, operation, existing, utcNow, cancellationToken))
                return StorageRetirementAdmission.InvalidTarget;
            await database.SaveChangesAsync(cancellationToken);
            await RemoveTransferredSourcesCoreAsync(tenantId, [], [source.Id], utcNow, cancellationToken);
            await RecalculateAsync(tenantId, [source.Provider], utcNow, cancellationToken);
            return StorageRetirementAdmission.Pending;
        }
        catch (Exception exception) when (exception is ConcurrencyConflictException or DbUpdateConcurrencyException)
        {
            await database.Database.CurrentTransaction!.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<int> RetireExpiredUploadsAsync(DateTime utcNow, int limit, CancellationToken cancellationToken)
    {
        RequireTransaction();
        RequireUtc(utcNow);
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
        await new StorageObjectReferenceRepository(database).FenceAsync(expired
            .Where(session => session.StorageObjectId.HasValue)
            .Select(session => session.StorageObjectId!.Value).Distinct().ToArray(), cancellationToken);
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
        RequireUtc(utcNow);
        var sources = await database.StorageObjects
            .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete]).AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.Purpose == StorageObjectPurposes.EventResource
                && item.OwningResourceKind == StorageOwningResourceKinds.EventResource
                && (objectIds.Contains(item.Id) || item.OwningResourceId.HasValue && resourceIds.Contains(item.OwningResourceId.Value)))
            .OrderBy(item => item.Id).ToArrayAsync(cancellationToken);
        var references = new StorageObjectReferenceRepository(database);
        await references.FenceAsync(sources.Select(item => item.Id).ToArray(), cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        var sessions = await database.StorageUploadSessions.IgnoreQueryFilters([QueryFilterNames.Tenant])
            .Where(item => item.TenantId == tenantId && item.Purpose == StorageObjectPurposes.EventResource
                && item.OwningResourceKind == StorageOwningResourceKinds.EventResource
                && (item.StorageObjectId.HasValue && objectIds.Contains(item.StorageObjectId.Value)
                    || item.OwningResourceId.HasValue && resourceIds.Contains(item.OwningResourceId.Value)))
            .ToArrayAsync(cancellationToken);
        foreach (var source in sources)
        {
            if (await references.HasBlockingReferencesAsync(source.Id, cancellationToken)
                || await references.HasBlockingHoldsAsync(source.Id, utcNow, cancellationToken)) continue;
            var fenced = (await SourceAsync(tenantId, source.Id, cancellationToken))!;
            var existing = await database.StorageObjectDeletionTombstones.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == source.Id, cancellationToken);
            if (!await TransferAsync(fenced, await SessionsAsync(source.Id, cancellationToken),
                await OperationAsync(source.Id, cancellationToken), existing, utcNow, cancellationToken))
                throw new InvalidOperationException("Retirement requires its exact original producer target.");
        }
        foreach (var session in sessions)
        {
            // Object-bearing custody is closed only by a successful transfer above.
            if (session.StorageObjectId is not null) continue;
            if (session.Status is StorageUploadSessionStates.Reserved or StorageUploadSessionStates.Uploading or StorageUploadSessionStates.Finalized)
                session.Fail("resource_storage_retired", null, utcNow);
        }
        await database.SaveChangesAsync(cancellationToken);
        await RecalculateAsync(tenantId, sources.Select(source => source.Provider)
            .Concat(sessions.Select(session => session.Provider)), utcNow, cancellationToken);
    }

    public async Task RemoveTransferredSourcesAsync(Guid tenantId, IReadOnlyCollection<Guid> resourceIds,
        IReadOnlyCollection<Guid> objectIds, CancellationToken cancellationToken)
    {
        RequireTransaction();
        try
        {
            await RemoveTransferredSourcesCoreAsync(tenantId, resourceIds, objectIds, DateTime.UtcNow, cancellationToken);
        }
        catch (Exception exception) when (exception is ConcurrencyConflictException or DbUpdateConcurrencyException)
        {
            await database.Database.CurrentTransaction!.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task RemoveTransferredSourcesCoreAsync(Guid tenantId, IReadOnlyCollection<Guid> resourceIds,
        IReadOnlyCollection<Guid> objectIds, DateTime utcNow, CancellationToken cancellationToken)
    {
        RequireTransaction();
        var sources = await database.StorageObjects
            .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete]).AsNoTracking()
            .Where(item => item.TenantId == tenantId && (objectIds.Contains(item.Id)
                || item.Purpose == StorageObjectPurposes.EventResource
                    && item.OwningResourceKind == StorageOwningResourceKinds.EventResource
                    && item.OwningResourceId.HasValue && resourceIds.Contains(item.OwningResourceId.Value)
                    && !database.OrganizationTenantEvidence.IgnoreQueryFilters(new[] { QueryFilterNames.Tenant })
                        .Any(evidence => evidence.DocumentStorageObjectId == item.Id)))
            .OrderBy(item => item.Id).ToArrayAsync(cancellationToken);
        var references = new StorageObjectReferenceRepository(database);
        await references.FenceAsync(sources.Select(item => item.Id).ToArray(), cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        sources = await database.StorageObjects
            .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete]).AsNoTracking()
            .Where(item => item.TenantId == tenantId && (objectIds.Contains(item.Id)
                || item.Purpose == StorageObjectPurposes.EventResource
                    && item.OwningResourceKind == StorageOwningResourceKinds.EventResource
                    && item.OwningResourceId.HasValue && resourceIds.Contains(item.OwningResourceId.Value)
                    && !database.OrganizationTenantEvidence.IgnoreQueryFilters(new[] { QueryFilterNames.Tenant })
                        .Any(evidence => evidence.DocumentStorageObjectId == item.Id)))
            .OrderBy(item => item.Id).ToArrayAsync(cancellationToken);
        var selectedIds = sources.Select(item => item.Id).Concat(objectIds).Distinct().ToArray();
        var sessions = await database.StorageUploadSessions.IgnoreQueryFilters([QueryFilterNames.Tenant]).AsNoTracking()
            .Where(item => selectedIds.Contains(item.Id)
                || item.StorageObjectId.HasValue && selectedIds.Contains(item.StorageObjectId.Value)
                || item.TenantId == tenantId && item.Purpose == StorageObjectPurposes.EventResource
                    && item.OwningResourceKind == StorageOwningResourceKinds.EventResource
                    && item.OwningResourceId.HasValue && resourceIds.Contains(item.OwningResourceId.Value)
                    && !database.OrganizationTenantEvidence.IgnoreQueryFilters(new[] { QueryFilterNames.Tenant })
                        .Any(evidence => evidence.DocumentStorageObjectId == (item.StorageObjectId ?? item.Id)))
            .ToArrayAsync(cancellationToken);
        var ids = sources.Select(item => item.Id).Concat(sessions.Where(item => item.StorageObjectId.HasValue)
            .Select(item => item.StorageObjectId!.Value)).Concat(objectIds).Distinct().ToArray();
        var operations = await database.Set<StorageProducerOperation>().AsNoTracking()
            .Where(item => ids.Contains(item.Id)).ToArrayAsync(cancellationToken);
        var tombstones = await database.StorageObjectDeletionTombstones.AsNoTracking()
            .Where(item => item.TenantId == tenantId && ids.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        foreach (var source in sources)
        {
            if (source.LifecycleState != StorageObjectLifecycleStates.DeleteRequested
                || !tombstones.TryGetValue(source.Id, out var tombstone)
                || tombstone.Provider != source.Provider || tombstone.ProviderBindingId != source.StorageProviderBindingId
                || tombstone.ObjectKey != source.ObjectKey
                || source.ProviderVersionId is not null && tombstone.ProviderObjectVersion != source.ProviderVersionId
                || await references.HasBlockingReferencesAsync(source.Id, cancellationToken)
                || await references.HasBlockingHoldsAsync(source.Id, utcNow, cancellationToken))
                throw new InvalidOperationException("Source removal requires detached storage with transferred deletion authority and no holds.");
        }
        foreach (var session in sessions)
        {
            if (session.TenantId != tenantId
                || session.Status is StorageUploadSessionStates.Reserved or StorageUploadSessionStates.Uploading or StorageUploadSessionStates.Finalized
                || (session.StorageObjectId ?? (session.ObjectKey is not null ? session.Id : (Guid?)null)) is { } objectId
                    && (!tombstones.TryGetValue(objectId, out var tombstone) || tombstone.Provider != session.Provider
                        || tombstone.ProviderBindingId != session.StorageProviderBindingId || tombstone.ObjectKey != session.ObjectKey
                        || session.ProviderVersionId is not null && tombstone.ProviderObjectVersion != session.ProviderVersionId
                        || !session.ProducerSettled && tombstone.State != Explore.Domain.Enums.StorageObjectDeletionState.AwaitingProducer))
                throw new InvalidOperationException("Source session removal requires closed accounting and transferred deletion authority.");
        }
        foreach (var operation in operations)
        {
            if (operation.TenantId != tenantId || !tombstones.TryGetValue(operation.Id, out var tombstone)
                || tombstone.Provider != operation.Provider || tombstone.ProviderBindingId != operation.ProviderBindingId
                || tombstone.ObjectKey != operation.ObjectKey
                || operation.ProviderVersionId is not null && tombstone.ProviderObjectVersion != operation.ProviderVersionId
                || !operation.ProducerSettled && tombstone.State != Explore.Domain.Enums.StorageObjectDeletionState.AwaitingProducer)
                throw new InvalidOperationException("Producer removal requires exact transferred deletion authority.");
        }
        foreach (var id in ids)
            if (await references.HasBlockingReferencesAsync(id, cancellationToken)
                || await references.HasBlockingHoldsAsync(id, utcNow, cancellationToken))
                throw new InvalidOperationException("Surviving references or holds prevent custody removal.");
        foreach (var operation in operations)
        {
            if (await database.Set<StorageProducerOperation>()
                .Where(item => item.Id == operation.Id && item.TenantId == tenantId
                    && item.ConcurrencyStamp == operation.ConcurrencyStamp)
                .ExecuteDeleteAsync(cancellationToken) != 1) throw SourceRemovalConflict();
            foreach (var entry in database.ChangeTracker.Entries<StorageProducerOperation>()
                .Where(item => item.Entity.Id == operation.Id).ToArray())
                entry.State = EntityState.Detached;
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
                    && database.StorageObjectDeletionTombstones.Any(tombstone => tombstone.Id == item.Id
                        && tombstone.TenantId == tenantId))
                .ExecuteDeleteAsync(cancellationToken);
            if (changed != 1) throw SourceRemovalConflict();
            DetachObject(source.Id);
        }
    }

    private Task<StorageObject?> SourceAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        database.StorageObjects.IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
            .AsNoTracking().SingleOrDefaultAsync(item => item.Id == id && item.TenantId == tenantId, cancellationToken);

    private Task<StorageUploadSession[]> SessionsAsync(Guid id, CancellationToken cancellationToken) =>
        database.StorageUploadSessions.IgnoreQueryFilters([QueryFilterNames.Tenant]).AsNoTracking()
            .Where(item => item.StorageObjectId == id || item.Id == id).ToArrayAsync(cancellationToken);

    private Task<StorageProducerOperation?> OperationAsync(Guid id, CancellationToken cancellationToken) =>
        database.Set<StorageProducerOperation>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    private async Task<bool> TransferAsync(StorageObject source, StorageUploadSession[] sessions,
        StorageProducerOperation? operation, StorageObjectDeletionTombstone? existing, DateTime utcNow,
        CancellationToken cancellationToken)
    {
        StorageObjectDeletionTombstone target;
        try { target = StorageRetirementTarget.Capture(source, sessions, operation, utcNow); }
        catch (ArgumentException) { return false; }
        if (!await database.StorageProviderBindings.AsNoTracking().AnyAsync(binding =>
            binding.Id == target.ProviderBindingId && binding.Provider == target.Provider, cancellationToken))
            return false;
        if (existing is not null && (existing.TenantId != target.TenantId || existing.Provider != target.Provider
            || existing.ProviderBindingId != target.ProviderBindingId || existing.ObjectKey != target.ObjectKey
            || target.ProviderObjectVersion is not null && existing.ProviderObjectVersion != target.ProviderObjectVersion
            || (sessions.Any(session => !session.ProducerSettled) || operation is { ProducerSettled: false })
                && existing.State != Explore.Domain.Enums.StorageObjectDeletionState.AwaitingProducer))
            return false;
        if (existing is null) database.StorageObjectDeletionTombstones.Add(target);
        DetachObject(source.Id);
        source.RequestDelete();
        database.StorageObjects.Update(source);
        foreach (var session in sessions)
        {
            DetachSession(session.Id);
            if (session.Status is StorageUploadSessionStates.Reserved or StorageUploadSessionStates.Uploading or StorageUploadSessionStates.Finalized)
            {
                session.Fail("resource_storage_retired", null, utcNow);
                database.StorageUploadSessions.Update(session);
            }
        }
        return true;
    }

    private async Task RecalculateAsync(Guid tenantId, IEnumerable<string> providers,
        DateTime utcNow, CancellationToken cancellationToken)
    {
        var counters = new StorageUsageCounterRepository(database);
        foreach (string provider in providers.Distinct().Order(StringComparer.Ordinal))
            await counters.RecalculateScopeAsync(tenantId, provider, utcNow, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
    }

    private static void RequireUtc(DateTime utcNow)
    {
        if (utcNow == default || utcNow.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Retirement admission requires a non-default server UTC instant.", nameof(utcNow));
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
        database.RecordStorageActivationProof(source.Id);
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
