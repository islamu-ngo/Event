namespace Explore.Persistence.Repositories;

using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

public sealed class LegalDocumentRepository(ExploreDbContext dbContext)
    : ILegalDocumentRepository
{
    public Guid CaptureConfigurationImportTransaction()
    {
        var transaction = dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Legal import requires the original serializable transaction.");
        if (!CurrentTransaction(transaction.TransactionId))
            throw new InvalidOperationException("Legal import transaction ownership is inconsistent.");
        return transaction.TransactionId;
    }

    public async Task<IReadOnlyList<LegalDocument>> GetForConfigurationExportAsync(
        LegalDocumentScope scope,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        string authorityKey = AuthorityKey(scope, tenantId);
        RequireExportTransaction();
        if (scope == LegalDocumentScope.Tenant && tenantId != dbContext.TenantFilterTenantId)
            throw new InvalidOperationException("Legal export tenant must match the authorized request tenant.");
        return await AggregateQuery(tracking: false)
            .Where(document => document.Scope == scope && document.TenantId == tenantId
                && document.AuthorityKey == authorityKey)
            .OrderBy(document => document.Kind)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LegalDocument>> GetForInstanceConfigurationExportAsync(
        IReadOnlyCollection<Guid> authorizedActiveTenantIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorizedActiveTenantIds);
        RequireExportTransaction();
        Guid[] tenantIds = authorizedActiveTenantIds.Distinct().Order().ToArray();
        if (tenantIds.Contains(Guid.Empty))
            throw new ArgumentException("Authorized tenant identifiers must be nonempty.", nameof(authorizedActiveTenantIds));
        // Legal aggregates have no global Tenant filter in the current model. Explicit scope,
        // target and authority-key predicates are therefore essential even for ordinary reads.
        // Only active-tenant metadata uses the existing named Tenant-filter read exception.
        Guid[] active = await dbContext.Tenants
            .IgnoreTenantFilter(TenantFilterBypassReasons.InstanceConfigurationManifestExport)
            .AsNoTracking()
            .Where(tenant => tenantIds.Contains(tenant.Id) && tenant.TenantStatus.IsActiveState)
            .Select(tenant => tenant.Id)
            .ToArrayAsync(cancellationToken);
        if (active.Length != tenantIds.Length)
            throw new InvalidOperationException("Legal instance export targets must remain active authorized tenants.");
        var documents = await AggregateQuery(tracking: false)
            .Where(document => document.Scope == LegalDocumentScope.Instance
                && document.TenantId == null && document.AuthorityKey == "instance")
            .OrderBy(document => document.Kind).ToListAsync(cancellationToken);
        foreach (Guid tenantId in tenantIds)
        {
            string authorityKey = AuthorityKey(LegalDocumentScope.Tenant, tenantId);
            documents.AddRange(await AggregateQuery(tracking: false)
                .Where(document => document.Scope == LegalDocumentScope.Tenant
                    && document.TenantId == tenantId && document.AuthorityKey == authorityKey)
                .OrderBy(document => document.Kind).ToArrayAsync(cancellationToken));
        }
        return documents.AsReadOnly();
    }

    public async Task FlushConfigurationImportInCurrentTransactionAsync(
        Guid transactionId,
        ConfigurationImportTarget target,
        IReadOnlyCollection<Guid> authorizedTenantIds,
        IReadOnlyList<LegalDocument> ownedLegalDocuments,
        CancellationToken cancellationToken)
    {
        var transaction = dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Legal import requires the original serializable transaction.");
        try
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(authorizedTenantIds);
            ArgumentNullException.ThrowIfNull(ownedLegalDocuments);
            if (!CurrentTransaction(transactionId)
                || target.Scope == ConfigurationImportScope.Tenant
                    && (target.TenantId != dbContext.TenantFilterTenantId || authorizedTenantIds.Count != 0)
                || target.Scope is not (ConfigurationImportScope.Instance or ConfigurationImportScope.Tenant)
                || authorizedTenantIds.Contains(Guid.Empty))
                throw new InvalidOperationException("Legal import transaction or target authority is inconsistent.");

            var owned = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var documentIds = new HashSet<Guid>();
            var authorityKinds = new HashSet<(string, LegalDocumentKind)>();
            dbContext.ChangeTracker.DetectChanges();
            foreach (var document in ownedLegalDocuments)
            {
                if (document is null || !documentIds.Add(document.Id)
                    || !authorityKinds.Add((document.AuthorityKey, document.Kind))
                    || document.AuthorityKey != AuthorityKey(document.Scope, document.TenantId)
                    || LegalDocumentKindCatalog.Get(document.Kind).Scope != document.Scope
                    || LegalDocumentKindCatalog.Get(document.Kind).OwnerRole != document.OwnerRole
                    || (target.Scope == ConfigurationImportScope.Tenant
                        ? document.Scope != LegalDocumentScope.Tenant || document.TenantId != target.TenantId
                        : document.Scope == LegalDocumentScope.Tenant
                            && !authorizedTenantIds.Contains(document.TenantId!.Value))
                    || document.Versions.Count(version => version.Version == document.CurrentVersion) != 1)
                    throw new InvalidOperationException("Legal import graph is outside its exact authorized scope.");
                Own(document, nameof(LegalDocument.Id), nameof(LegalDocument.Scope),
                    nameof(LegalDocument.TenantId), nameof(LegalDocument.AuthorityKey),
                    nameof(LegalDocument.Kind), nameof(LegalDocument.OwnerRole));
                var versions = new Dictionary<Guid, LegalDocumentVersion>();
                foreach (var version in document.Versions)
                {
                    if (version.LegalDocumentId != document.Id || !versions.TryAdd(version.Id, version)
                        || version.LegalDocument is not null && !ReferenceEquals(version.LegalDocument, document))
                        throw new InvalidOperationException("Legal import version has foreign aggregate ownership.");
                    Own(version, nameof(LegalDocumentVersion.Id), nameof(LegalDocumentVersion.LegalDocumentId),
                        nameof(LegalDocumentVersion.Version));
                    foreach (var source in version.Sources)
                    {
                        if (source.LegalDocumentVersionId != version.Id
                            || !ReferenceEquals(source.LegalDocumentVersion, version))
                            throw new InvalidOperationException("Legal import source has foreign version ownership.");
                        Own(source, nameof(LegalDocumentLocalizedSource.Id),
                            nameof(LegalDocumentLocalizedSource.LegalDocumentVersionId));
                    }
                }
                foreach (var publication in document.Publications)
                {
                    if (publication.LegalDocumentId != document.Id
                        || !versions.TryGetValue(publication.LegalDocumentVersionId, out var version)
                        || publication.Version != version.Version
                        || publication.LegalDocument is not null && !ReferenceEquals(publication.LegalDocument, document)
                        || publication.LegalDocumentVersion is not null
                            && !ReferenceEquals(publication.LegalDocumentVersion, version))
                        throw new InvalidOperationException("Legal import publication has foreign graph ownership.");
                    Own(publication, nameof(LegalDocumentPublication.Id), nameof(LegalDocumentPublication.LegalDocumentId),
                        nameof(LegalDocumentPublication.LegalDocumentVersionId), nameof(LegalDocumentPublication.Version));
                }
            }
            var pending = dbContext.ChangeTracker.Entries()
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .ToArray();
            if (pending.Any(entry => entry.State == EntityState.Deleted || !owned.Contains(entry.Entity)))
                throw new InvalidOperationException("Legal import cannot flush deleted or foreign pending work.");
            if (pending.Length > 0)
                await dbContext.SaveChangesAsync(cancellationToken);
            if (!CurrentTransaction(transactionId) || dbContext.ChangeTracker.HasChanges())
                throw new InvalidOperationException("Legal import lost its original transaction or clean tracker.");

            void Own(object entity, params string[] coordinates)
            {
                EntityEntry entry = dbContext.Entry(entity);
                if (!owned.Add(entity) || entry.State is EntityState.Detached or EntityState.Deleted
                    || coordinates.Any(name => entry.Property(name).CurrentValue is Guid id && id == Guid.Empty)
                    || entry.State != EntityState.Added
                        && coordinates.Any(name => !Equals(entry.Property(name).OriginalValue, entry.Property(name).CurrentValue)))
                    throw new InvalidOperationException("Legal import requires exact tracked graph ownership.");
            }
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private void RequireExportTransaction()
    {
        if (dbContext.Database.CurrentTransaction is not { } transaction
            || !CurrentTransaction(transaction.TransactionId) || dbContext.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Legal export requires a clean serializable authorized read transaction.");
    }

    private bool CurrentTransaction(Guid transactionId) =>
        dbContext.Database.CurrentTransaction is { } transaction
        && transaction.TransactionId == transactionId
        && transaction.GetDbTransaction().IsolationLevel == IsolationLevel.Serializable
        && transaction.GetDbTransaction().Connection == dbContext.Database.GetDbConnection()
        && !dbContext.IsTenantFilterBypassed;

    public Task<LegalDocument?> GetForUpdateAsync(
        LegalDocumentScope scope,
        Guid? tenantId,
        LegalDocumentKind kind,
        CancellationToken cancellationToken) =>
        AggregateQuery(tracking: true)
            .SingleOrDefaultAsync(
                document => document.AuthorityKey == AuthorityKey(scope, tenantId)
                    && document.Kind == kind,
                cancellationToken);

    public Task<LegalDocument?> GetByIdForUpdateAsync(
        Guid legalDocumentId,
        LegalDocumentScope scope,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(legalDocumentId, Guid.Empty);
        string authorityKey = AuthorityKey(scope, tenantId);
        return AggregateQuery(tracking: true)
            .SingleOrDefaultAsync(
                document => document.Id == legalDocumentId
                    && document.AuthorityKey == authorityKey,
                cancellationToken);
    }

    public Task<LegalDocument?> GetPublishedAsync(
        LegalDocumentScope scope,
        Guid? tenantId,
        LegalDocumentKind kind,
        CancellationToken cancellationToken) =>
        AggregateQuery(tracking: false)
            .SingleOrDefaultAsync(
                document => document.AuthorityKey == AuthorityKey(scope, tenantId)
                    && document.Kind == kind
                    && document.Publications.Any(publication =>
                        publication.LifecycleState
                            == LegalDocumentLifecycleState.Published),
                cancellationToken);

    public async Task AddAsync(
        LegalDocument legalDocument,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(legalDocument);
        await dbContext.LegalDocuments.AddAsync(legalDocument, cancellationToken);
    }

    private IQueryable<LegalDocument> AggregateQuery(bool tracking)
    {
        IQueryable<LegalDocument> query = dbContext.LegalDocuments
            .Include(document => document.Versions)
                .ThenInclude(version => version.Sources)
            .Include(document => document.Publications);
        return tracking ? query : query.AsNoTracking();
    }

    private static string AuthorityKey(
        LegalDocumentScope scope,
        Guid? tenantId) =>
        scope switch
        {
            LegalDocumentScope.Instance when tenantId is null => "instance",
            LegalDocumentScope.Tenant when tenantId is { } targetTenantId
                && targetTenantId != Guid.Empty => $"tenant:{targetTenantId:N}",
            _ => throw new ArgumentException(
                "Legal document target scope and tenant are inconsistent.",
                nameof(tenantId))
        };
}
