using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class StorageObjectRepository : GenericRepository<StorageObject, Guid>, IStorageObjectRepository, IStorageProducerOperationRepository
{
    private readonly ExploreDbContext _dbContext;

    public StorageObjectRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddProducerAsync(StorageProducerOperation producer, CancellationToken cancellationToken)
    {
        RequireProducerTransaction();
        await _dbContext.Set<StorageProducerOperation>().AddAsync(producer, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<StorageProducerOperation?> FenceProducerAsync(
        Guid id, Guid tenantId, CancellationToken cancellationToken)
    {
        RequireProducerTransaction();
        var producer = await _dbContext.Set<StorageProducerOperation>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == id && value.TenantId == tenantId, cancellationToken);
        if (producer is null) return null;
        var stamp = Guid.CreateVersion7();
        int changed = await _dbContext.Set<StorageProducerOperation>()
            .Where(value => value.Id == id && value.TenantId == tenantId
                && value.ConcurrencyStamp == producer.ConcurrencyStamp
                && !_dbContext.StorageObjectDeletionTombstones.Any(work => work.Id == id))
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.ConcurrencyStamp, stamp),
                cancellationToken);
        if (changed != 1)
            throw new ConcurrencyConflictException(ConcurrencyConflictException.ConcurrentUpdate,
                "Storage production changed concurrently.");
        foreach (var entry in _dbContext.ChangeTracker.Entries<StorageProducerOperation>()
            .Where(entry => entry.Entity.Id == id).ToArray())
            entry.State = EntityState.Detached;
        return await _dbContext.Set<StorageProducerOperation>()
            .SingleAsync(value => value.Id == id && value.TenantId == tenantId, cancellationToken);
    }

    public async Task SettleProducerAsync(
        StorageProducerOperation identity, string? providerVersion, CancellationToken cancellationToken)
    {
        RequireProducerTransaction();
        var tombstones = new StorageObjectDeletionTombstoneRepository(_dbContext);
        var tombstone = await tombstones.GetByIdAsync(identity.Id, cancellationToken);
        if (tombstone is not null)
        {
            if (tombstone.TenantId != identity.TenantId || tombstone.Provider != identity.Provider)
                throw new InvalidOperationException("storage_producer_identity_mismatch");
            await tombstones.TrySettleProducerAsync(identity.Id, identity.ProviderBindingId,
                identity.ObjectKey, providerVersion, DateTime.UtcNow, cancellationToken);
            return;
        }
        var producer = await FenceProducerAsync(identity.Id, identity.TenantId, cancellationToken)
            ?? throw new InvalidOperationException("storage_producer_unavailable");
        producer.Settle(identity.ProviderBindingId, identity.Provider, identity.ObjectKey, providerVersion);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteProducerAsync(
        StorageProducerOperation producer, StorageObject storageObject, CancellationToken cancellationToken)
    {
        RequireProducerTransaction();
        if (!producer.ProducerSettled || storageObject.Id != producer.Id
            || storageObject.TenantId != producer.TenantId || storageObject.Provider != producer.Provider
            || storageObject.StorageProviderBindingId != producer.ProviderBindingId
            || storageObject.ObjectKey != producer.ObjectKey || storageObject.ProviderVersionId != producer.ProviderVersionId
            || _dbContext.Entry(producer).State != EntityState.Unchanged)
            throw new InvalidOperationException("storage_producer_identity_mismatch");
        _dbContext.Set<StorageProducerOperation>().Remove(producer);
        await _dbContext.StorageObjects.AddAsync(storageObject, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RetireProducerAsync(Guid id, Guid tenantId, DateTime utcNow, CancellationToken cancellationToken)
    {
        var producer = await FenceProducerAsync(id, tenantId, cancellationToken);
        // Activation or an earlier retirement already transferred this producer's authority.
        if (producer is null) return;
        await _dbContext.StorageObjectDeletionTombstones.AddAsync(producer.Retire(utcNow), cancellationToken);
        _dbContext.Set<StorageProducerOperation>().Remove(producer);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private void RequireProducerTransaction()
    {
        if (_dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Storage production requires a caller-owned transaction.");
    }

    public Task<StorageObject?> GetForAuthorizationAsync(Guid id, Guid tenantId, CancellationToken cancellationToken) =>
        _dbContext.StorageObjects
            .AsNoTracking()
            .FirstOrDefaultAsync(storageObject => storageObject.Id == id && storageObject.TenantId == tenantId,
                cancellationToken);

    public Task<StorageObject?> GetForGenericAccessAsync(Guid id, CancellationToken cancellationToken) =>
        GenericAccessQuery().FirstOrDefaultAsync(storageObject => storageObject.Id == id, cancellationToken);

    private IQueryable<StorageObject> GenericAccessQuery() =>
        WithoutResourceOwnership(_dbContext.StorageObjects.AsNoTracking());

    private IQueryable<StorageObject> WithoutResourceOwnership(IQueryable<StorageObject> query) =>
        query
            .Where(storageObject =>
                !storageObject.IsDeleted &&
                storageObject.Purpose != StorageObjectPurposes.EventResource &&
                storageObject.OwningResourceKind != StorageOwningResourceKinds.EventResource &&
                !_dbContext.EventResources
                    .IgnoreQueryFilters(new[] { QueryFilterNames.SoftDelete })
                    .Any(resource => resource.TenantId == storageObject.TenantId &&
                        resource.StorageObjectId == storageObject.Id) &&
                !_dbContext.StorageObjectDeletionTombstones.Any(work => work.Id == storageObject.Id));

    public async Task<List<StorageObject>> GetFilesWithDetails()
    {
        return await GenericAccessQuery()
            .Include(f => f.FileType)
            .Include(f => f.Tenant)
            .Include(f => f.Actor)
                .ThenInclude(a => a!.Pii)
            .ToListAsync();
    }

    public async Task<StorageObject?> GetFileWithDetails(Guid id)
    {
        return await GenericAccessQuery()
            .Include(f => f.FileType)
            .Include(f => f.Tenant)
            .Include(f => f.Actor)
                .ThenInclude(a => a!.Pii)
            .FirstOrDefaultAsync(f => f.Id == id);
    }

    public async Task<(List<StorageObject> Items, int TotalCount)> GetFilesWithDetailsPaged(int pageNumber, int pageSize)
    {
        var query = GenericAccessQuery()
            .Include(f => f.FileType)
            .Include(f => f.Actor)
                .ThenInclude(a => a!.Pii)
            .OrderByDescending(f => f.Id);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<StorageObject>> GetAllForInstanceStorageReportAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.StorageObjects
            .AsNoTracking()
            .IgnoreTenantFilter(TenantFilterBypassReasons.InstanceStorageAdministration)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StorageObject>> ListActiveForReconciliationAsync(
        DateTime createdBeforeUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return [];
        }

        return await BaseReconciliationQuery()
            .Where(storageObject =>
                storageObject.LifecycleState == StorageObjectLifecycleStates.Active &&
                storageObject.CreatedAt <= createdBeforeUtc)
            .OrderBy(storageObject => storageObject.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StorageObject>> ListDeleteEligibleForReconciliationAsync(
        DateTime deleteBeforeUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return [];
        }

        return await BaseReconciliationQuery()
            .Where(storageObject =>
                (storageObject.LifecycleState == StorageObjectLifecycleStates.Quarantined &&
                 (storageObject.QuarantinedAt ?? storageObject.UpdatedAt ?? storageObject.CreatedAt) <= deleteBeforeUtc) ||
                (storageObject.LifecycleState == StorageObjectLifecycleStates.DeleteRequested &&
                 (storageObject.UpdatedAt ?? storageObject.CreatedAt) <= deleteBeforeUtc))
            .OrderBy(storageObject => storageObject.UpdatedAt ?? storageObject.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StorageObject>> ListDeleteRequestedForResourceAsync(
        Guid tenantId,
        string owningResourceKind,
        Guid owningResourceId,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit <= 0 || tenantId == Guid.Empty || owningResourceId == Guid.Empty || string.IsNullOrWhiteSpace(owningResourceKind))
        {
            return [];
        }

        return await WithoutResourceOwnership(_dbContext.StorageObjects
            .IgnoreTenantFilter(TenantFilterBypassReasons.InstanceStorageAdministration))
            .Where(storageObject =>
                !storageObject.IsDeleted &&
                storageObject.TenantId == tenantId &&
                !_dbContext.OrganizationTenantEvidence
                    .IgnoreTenantFilter(TenantFilterBypassReasons.InstanceStorageAdministration)
                    .Any(evidence => evidence.DocumentStorageObjectId == storageObject.Id) &&
                storageObject.LifecycleState == StorageObjectLifecycleStates.DeleteRequested &&
                (storageObject.Provider == StorageProviders.Local ||
                 storageObject.Provider == StorageProviders.S3Compatible) &&
                storageObject.OwningResourceKind == owningResourceKind &&
                storageObject.OwningResourceId == owningResourceId)
            .OrderBy(storageObject => storageObject.UpdatedAt ?? storageObject.CreatedAt)
            .ThenBy(storageObject => storageObject.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListKnownObjectKeysAsync(
        IReadOnlyCollection<Guid> bindingIds,
        IReadOnlyCollection<string> objectKeys,
        CancellationToken cancellationToken)
    {
        if (bindingIds.Count == 0 || objectKeys.Count == 0)
        {
            return [];
        }

        return await _dbContext.StorageObjects
            .AsNoTracking()
            .IgnoreTenantFilter(TenantFilterBypassReasons.InstanceStorageAdministration)
            .Where(storageObject =>
                storageObject.StorageProviderBindingId.HasValue &&
                bindingIds.Contains(storageObject.StorageProviderBindingId.Value) &&
                storageObject.ObjectKey != null &&
                objectKeys.Contains(storageObject.ObjectKey))
            .Select(storageObject => storageObject.ObjectKey!)
            .Union(_dbContext.StorageObjectDeletionTombstones.AsNoTracking()
                .Where(work => bindingIds.Contains(work.ProviderBindingId) && objectKeys.Contains(work.ObjectKey))
                .Select(work => work.ObjectKey))
            .Union(_dbContext.StorageUploadSessions.AsNoTracking()
                .IgnoreTenantFilter(TenantFilterBypassReasons.InstanceStorageAdministration)
                .Where(session => session.StorageProviderBindingId.HasValue
                    && bindingIds.Contains(session.StorageProviderBindingId.Value)
                    && session.ObjectKey != null && objectKeys.Contains(session.ObjectKey))
                .Select(session => session.ObjectKey!))
            .Union(_dbContext.Set<StorageProducerOperation>().AsNoTracking()
                .Where(producer => bindingIds.Contains(producer.ProviderBindingId) && objectKeys.Contains(producer.ObjectKey))
                .Select(producer => producer.ObjectKey))
            .ToListAsync(cancellationToken);
    }

    public Task<StorageObject?> GetEvidenceDocumentAsync(Guid id, CancellationToken cancellationToken)
    {
        return _dbContext.StorageObjects
            .AsNoTracking()
            .Include(storageObject => storageObject.FileType)
            .FirstOrDefaultAsync(storageObject => storageObject.Id == id, cancellationToken);
    }

    public Task<bool> IsRetainedEvidenceAsync(Guid id, CancellationToken cancellationToken)
    {
        return _dbContext.OrganizationTenantEvidence
            .AsNoTracking()
            .AnyAsync(evidence => evidence.DocumentStorageObjectId == id, cancellationToken);
    }

    public Task<bool> IsRegistrationAnswerFileQuarantinedAsync(Guid id, CancellationToken cancellationToken)
    {
        return _dbContext.RegistrationAnswerFiles
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .AsNoTracking()
            .AnyAsync(file =>
                file.StorageObjectId == id &&
                file.QuarantineState != RegistrationAnswerFileQuarantineStates.Released,
                cancellationToken);
    }

    public Task<RegistrationAnswerFile?> GetRegistrationAnswerFileAsync(
        Guid storageObjectId, Guid tenantId, CancellationToken cancellationToken) =>
        _dbContext.RegistrationAnswerFiles
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .AsNoTracking()
            .SingleOrDefaultAsync(file => file.TenantId == tenantId && file.StorageObjectId == storageObjectId,
                cancellationToken);

    public async Task<RegistrationOrder?> GetRegistrationContentOrderAsync(
        StorageObject storageObject, RegistrationAnswerFile? answerFile, CancellationToken cancellationToken)
    {
        Guid? submissionId;
        if (storageObject.OwningResourceKind == "registration_submission_sink" && answerFile is null)
        {
            submissionId = storageObject.OwningResourceId;
        }
        else if (answerFile is { IsDeleted: false } &&
            answerFile.TenantId == storageObject.TenantId && answerFile.StorageObjectId == storageObject.Id &&
            (storageObject.OwningResourceKind is null && storageObject.OwningResourceId is null ||
             storageObject.OwningResourceKind == RegistrationAnswerFileStorageOwnership.ResourceKind &&
             storageObject.OwningResourceId == answerFile.Id))
        {
            submissionId = answerFile.RegistrationSubmissionId;
        }
        else
        {
            return null;
        }

        Guid? fileEventId = answerFile?.EventId;
        return await (from submission in _dbContext.RegistrationSubmissions.AsNoTracking()
                      join order in _dbContext.RegistrationOrders.AsNoTracking()
                          on new { submission.TenantId, submission.EventId, Id = submission.RegistrationOrderId }
                          equals new { order.TenantId, order.EventId, order.Id }
                      where submission.TenantId == storageObject.TenantId && submission.Id == submissionId &&
                          (fileEventId == null || submission.EventId == fileEventId)
                      select order).SingleOrDefaultAsync(cancellationToken);
    }

    private IQueryable<StorageObject> BaseReconciliationQuery()
    {
        return WithoutResourceOwnership(_dbContext.StorageObjects
            .IgnoreTenantFilter(TenantFilterBypassReasons.InstanceStorageAdministration))
            .Where(storageObject =>
                !storageObject.IsDeleted &&
                !_dbContext.OrganizationTenantEvidence
                    .IgnoreTenantFilter(TenantFilterBypassReasons.InstanceStorageAdministration)
                    .Any(evidence => evidence.DocumentStorageObjectId == storageObject.Id) &&
                storageObject.ObjectKey != null &&
                (storageObject.Provider == StorageProviders.Local ||
                 storageObject.Provider == StorageProviders.S3Compatible));
    }
}
