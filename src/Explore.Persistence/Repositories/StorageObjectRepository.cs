using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class StorageObjectRepository : GenericRepository<StorageObject, Guid>, IStorageObjectRepository
{
    private readonly ExploreDbContext _dbContext;

    public StorageObjectRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<StorageObject>> GetFilesWithDetails()
    {
        return await _dbContext.StorageObjects
            .AsNoTracking()
            .Include(f => f.FileType)
            .Include(f => f.Tenant)
            .Include(f => f.Actor)
                .ThenInclude(a => a!.Pii)
            .ToListAsync();
    }

    public async Task<StorageObject?> GetFileWithDetails(Guid id)
    {
        return await _dbContext.StorageObjects
            .AsNoTracking()
            .Include(f => f.FileType)
            .Include(f => f.Tenant)
            .Include(f => f.Actor)
                .ThenInclude(a => a!.Pii)
            .FirstOrDefaultAsync(f => f.Id == id);
    }

    public async Task<(List<StorageObject> Items, int TotalCount)> GetFilesWithDetailsPaged(int pageNumber, int pageSize)
    {
        var query = _dbContext.StorageObjects
            .AsNoTracking()
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

        return await _dbContext.StorageObjects
            .IgnoreTenantFilter(TenantFilterBypassReasons.InstanceStorageAdministration)
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
        string provider,
        IReadOnlyCollection<string> objectKeys,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(provider) || objectKeys.Count == 0)
        {
            return [];
        }

        return await _dbContext.StorageObjects
            .AsNoTracking()
            .IgnoreTenantFilter(TenantFilterBypassReasons.InstanceStorageAdministration)
            .Where(storageObject =>
                storageObject.Provider == provider &&
                storageObject.ObjectKey != null &&
                objectKeys.Contains(storageObject.ObjectKey))
            .Select(storageObject => storageObject.ObjectKey!)
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
        return _dbContext.StorageObjects
            .IgnoreTenantFilter(TenantFilterBypassReasons.InstanceStorageAdministration)
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
