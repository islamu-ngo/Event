using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Explore.Persistence;

public partial class ExploreDbContext
{
    private Guid? _storageReferenceTransactionId;
    private Guid? _failedStorageReferenceTransactionId;
    private readonly HashSet<Guid> _fencedStorageReferences = [];
    private (Guid TenantId, Guid ObjectId)? _lastStorageReferenceFence;
    private Guid? _storageActivationTransactionId;
    private readonly HashSet<Guid> _authorizedStorageActivations = [];

    /// <summary>Records the settled-session activation CAS for this transaction only.</summary>
    internal void RecordStorageActivationProof(Guid objectId)
    {
        var transaction = Database.CurrentTransaction
            ?? throw new InvalidOperationException("Storage activation proof requires the owning transaction.");
        if (_storageActivationTransactionId != transaction.TransactionId)
        {
            _storageActivationTransactionId = transaction.TransactionId;
            _authorizedStorageActivations.Clear();
        }
        _authorizedStorageActivations.Add(objectId);
    }

    internal bool StorageReferenceTransactionFailed =>
        Database.CurrentTransaction is { } transaction
        && _failedStorageReferenceTransactionId == transaction.TransactionId;

    private List<(EntityEntry Entry, IProperty Property)> StorageReferenceCarriers()
    {
        ChangeTracker.DetectChanges();
        return ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && entry.Entity is not StorageUploadSession)
            .SelectMany(entry => entry.Metadata.GetForeignKeys()
                .Where(key => key.PrincipalEntityType.ClrType == typeof(StorageObject))
                .SelectMany(key => key.Properties.Where((_, index) =>
                    key.PrincipalKey.Properties[index].Name == nameof(StorageObject.Id)))
                .Distinct()
                .Select(property => (entry, property)))
            .ToList();
    }

    private static void CollectStorageReference(
        Dictionary<Guid, bool> references,
        EntityEntry entry,
        IProperty property,
        PropertyValues? persisted)
    {
        Guid? previous = persisted?[property.Name] as Guid?;
        Guid? current = entry.State == EntityState.Deleted
            ? null : entry.Property(property.Name).CurrentValue as Guid?;
        if (previous is { } oldId)
            references.TryAdd(oldId, false);
        if (current is { } id)
            references[id] = references.GetValueOrDefault(id)
                || entry.State == EntityState.Added || previous != current;
    }

    private int SaveWithStorageReferences(Func<int> persist)
    {
        var carriers = StorageReferenceCarriers();
        if (carriers.Count == 0 || !Database.IsRelational())
            return persist();
        using var owned = Database.CurrentTransaction is null ? Database.BeginTransaction() : null;
        try
        {
            var references = new Dictionary<Guid, bool>();
            foreach (var group in carriers.GroupBy(carrier => carrier.Entry))
            {
                PropertyValues? persisted = group.Key.State == EntityState.Added
                    ? null : group.Key.GetDatabaseValues();
                foreach (var carrier in group)
                    CollectStorageReference(references, carrier.Entry, carrier.Property, persisted);
            }
            var sources = StorageReferenceSources(references.Keys).ToList();
            ValidateMissingStorageReferences(references, sources);
            foreach (var source in sources.OrderBy(item => item.TenantId).ThenBy(item => item.Id))
            {
                if (EnrollStorageReferenceFence(source))
                {
                    Guid stamp = Guid.CreateVersion7();
                    if (StorageReferenceSources([source.Id]).Where(item => item.ConcurrencyStamp == source.ConcurrencyStamp)
                        .ExecuteUpdate(setters => setters.SetProperty(item => item.ConcurrencyStamp, stamp)) != 1)
                        throw StorageReferenceConflict();
                    RefreshStorageReferenceStamp(source.Id, stamp);
                }
                if (references[source.Id])
                    ValidateStorageAttachment(source,
                        StorageObjectDeletionTombstones.Any(item => item.Id == source.Id));
            }
            int result = persist();
            owned?.Commit();
            return result;
        }
        catch
        {
            PoisonStorageReferenceTransaction();
            owned?.Rollback();
            throw;
        }
    }

    private async Task<int> SaveWithStorageReferencesAsync(
        Func<Task<int>> persist, CancellationToken cancellationToken)
    {
        var carriers = StorageReferenceCarriers();
        if (carriers.Count == 0 || !Database.IsRelational())
            return await persist();
        await using var owned = Database.CurrentTransaction is null
            ? await Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            var references = new Dictionary<Guid, bool>();
            foreach (var group in carriers.GroupBy(carrier => carrier.Entry))
            {
                PropertyValues? persisted = group.Key.State == EntityState.Added
                    ? null : await group.Key.GetDatabaseValuesAsync(cancellationToken);
                foreach (var carrier in group)
                    CollectStorageReference(references, carrier.Entry, carrier.Property, persisted);
            }
            var sources = await StorageReferenceSources(references.Keys).ToListAsync(cancellationToken);
            ValidateMissingStorageReferences(references, sources);
            await FenceStorageReferenceRowsAsync(sources, cancellationToken);
            foreach (var source in sources)
            {
                if (references[source.Id])
                    ValidateStorageAttachment(source,
                        await StorageObjectDeletionTombstones.AnyAsync(item => item.Id == source.Id, cancellationToken));
            }
            int result = await persist();
            if (owned is not null)
                await owned.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            PoisonStorageReferenceTransaction();
            if (owned is not null)
                await owned.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    internal async Task<IReadOnlyList<StorageObject>> FenceStorageObjectsAsync(
        IReadOnlyCollection<Guid> objectIds, CancellationToken cancellationToken)
    {
        if (Database.CurrentTransaction is null)
            throw new InvalidOperationException("Storage reference fencing requires the caller's transaction.");
        try
        {
            var ids = objectIds.Distinct().ToArray();
            var sources = await StorageReferenceSources(ids).ToListAsync(cancellationToken);
            if (sources.Count != ids.Length)
                throw StorageReferenceConflict();
            await FenceStorageReferenceRowsAsync(sources, cancellationToken);
            return sources;
        }
        catch
        {
            PoisonStorageReferenceTransaction();
            throw;
        }
    }

    private async Task FenceStorageReferenceRowsAsync(
        IReadOnlyCollection<StorageObject> sources, CancellationToken cancellationToken)
    {
        foreach (var source in sources.OrderBy(item => item.TenantId).ThenBy(item => item.Id))
        {
            if (!EnrollStorageReferenceFence(source))
                continue;
            Guid stamp = Guid.CreateVersion7();
            if (await StorageReferenceSources([source.Id]).Where(item => item.ConcurrencyStamp == source.ConcurrencyStamp)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ConcurrencyStamp, stamp),
                    cancellationToken) != 1)
                throw StorageReferenceConflict();
            source.ConcurrencyStamp = stamp;
            RefreshStorageReferenceStamp(source.Id, stamp);
        }
    }

    private IQueryable<StorageObject> StorageReferenceSources(IEnumerable<Guid> ids) =>
        StorageObjects.IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
            .AsNoTracking().Where(item => ids.Contains(item.Id));

    private void ValidateMissingStorageReferences(Dictionary<Guid, bool> references, List<StorageObject> sources)
    {
        var inserted = ChangeTracker.Entries<StorageObject>()
            .Where(entry => entry.State == EntityState.Added).Select(entry => entry.Entity.Id).ToHashSet();
        if (references.Any(reference => reference.Value
            && !inserted.Contains(reference.Key)
            && !sources.Any(source => source.Id == reference.Key)))
            throw StorageReferenceConflict();
    }

    private bool EnrollStorageReferenceFence(StorageObject source)
    {
        Guid transactionId = Database.CurrentTransaction!.TransactionId;
        if (_storageReferenceTransactionId != transactionId)
        {
            _storageReferenceTransactionId = transactionId;
            _fencedStorageReferences.Clear();
            _lastStorageReferenceFence = null;
        }
        if (StorageReferenceTransactionFailed)
            throw StorageReferenceConflict();
        if (_fencedStorageReferences.Contains(source.Id))
            return false;
        var next = (source.TenantId, source.Id);
        if (_lastStorageReferenceFence is { } previous && next.CompareTo(previous) < 0)
            throw new InvalidOperationException("The full storage reference set must be fenced in tenant/object order.");
        _fencedStorageReferences.Add(source.Id);
        _lastStorageReferenceFence = next;
        return true;
    }

    private void RefreshStorageReferenceStamp(Guid objectId, Guid stamp)
    {
        foreach (var entry in ChangeTracker.Entries<StorageObject>().Where(entry => entry.Entity.Id == objectId))
        {
            entry.Property(item => item.ConcurrencyStamp).OriginalValue = stamp;
            entry.Property(item => item.ConcurrencyStamp).CurrentValue = stamp;
        }
    }

    private void ValidateStorageAttachment(StorageObject source, bool retirementCommitted)
    {
        var pending = ChangeTracker.Entries<StorageObject>()
            .SingleOrDefault(entry => entry.Entity.Id == source.Id)?.Entity;
        if (retirementCommitted || source.IsDeleted
            || (pending?.LifecycleState ?? source.LifecycleState) != StorageObjectLifecycleStates.Active
            || source.LifecycleState != StorageObjectLifecycleStates.Active
                && (_storageActivationTransactionId != Database.CurrentTransaction?.TransactionId
                    || !_authorizedStorageActivations.Contains(source.Id)))
            throw StorageReferenceConflict();
    }

    private void PoisonStorageReferenceTransaction()
    {
        _failedStorageReferenceTransactionId = Database.CurrentTransaction?.TransactionId;
        if (IdentityFenceOwnsTransaction)
            IdentityFenceTransactionFailed = true;
    }

    private static ConcurrencyConflictException StorageReferenceConflict() =>
        new(ConcurrencyConflictException.ConcurrentUpdate,
            "Storage retirement or a concurrent reference mutation prevents this attachment.");
}
