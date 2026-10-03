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
    private readonly Dictionary<Guid, Guid> _fencedStorageReferences = [];
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
                .Where(property => entry.State != EntityState.Added
                    || entry.Property(property.Name).CurrentValue is Guid)
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

    private int SaveWithStorageReferences(Func<bool, int> persist, bool acceptAllChangesOnSuccess)
    {
        if (Database.IsRelational() && StorageReferenceTransactionFailed)
            throw StorageReferenceConflict();
        var carriers = StorageReferenceCarriers();
        if (carriers.Count == 0 || !Database.IsRelational())
            return persist(acceptAllChangesOnSuccess);
        if (Database.CurrentTransaction is not null)
            return PersistStorageReferences(carriers, () => persist(acceptAllChangesOnSuccess));

        var stamps = CaptureTrackedStorageStamps();
        Dictionary<Guid, Guid> commitStamps = [];
        try
        {
            int result = Database.CreateExecutionStrategy().ExecuteInTransaction(
                () =>
                {
                    commitStamps = [];
                    RestoreTrackedStorageStamps(stamps);
                    int saved = PersistStorageReferences(carriers, () => persist(false));
                    commitStamps = CaptureStorageCommitStamps();
                    return saved;
                },
                () => commitStamps.Any(stamp => StorageReferenceSources([stamp.Key])
                    .Any(source => source.ConcurrencyStamp == stamp.Value)));
            if (acceptAllChangesOnSuccess)
                ChangeTracker.AcceptAllChanges();
            return result;
        }
        catch
        {
            RestoreTrackedStorageStamps(stamps);
            throw;
        }
    }

    private int PersistStorageReferences(
        List<(EntityEntry Entry, IProperty Property)> carriers, Func<int> persist)
    {
        try
        {
            if (StorageReferenceTransactionFailed)
                throw StorageReferenceConflict();
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
                    _fencedStorageReferences[source.Id] = stamp;
                    RefreshStorageReferenceStamp(source.Id, stamp);
                }
                if (references[source.Id])
                    ValidateStorageAttachment(source,
                        StorageObjectDeletionTombstones.Any(item => item.Id == source.Id));
            }
            return persist();
        }
        catch
        {
            PoisonStorageReferenceTransaction();
            throw;
        }
    }

    private async Task<int> SaveWithStorageReferencesAsync(
        Func<bool, Task<int>> persist, bool acceptAllChangesOnSuccess, CancellationToken cancellationToken)
    {
        if (Database.IsRelational() && StorageReferenceTransactionFailed)
            throw StorageReferenceConflict();
        var carriers = StorageReferenceCarriers();
        if (carriers.Count == 0 || !Database.IsRelational())
            return await persist(acceptAllChangesOnSuccess);
        if (Database.CurrentTransaction is not null)
            return await PersistStorageReferencesAsync(
                carriers, () => persist(acceptAllChangesOnSuccess), cancellationToken);

        var stamps = CaptureTrackedStorageStamps();
        Dictionary<Guid, Guid> commitStamps = [];
        try
        {
            int result = await Database.CreateExecutionStrategy().ExecuteInTransactionAsync(
                this,
                async (_, token) =>
                {
                    commitStamps = [];
                    RestoreTrackedStorageStamps(stamps);
                    int saved = await PersistStorageReferencesAsync(carriers, () => persist(false), token);
                    commitStamps = CaptureStorageCommitStamps();
                    return saved;
                },
                async (_, token) =>
                {
                    foreach (var stamp in commitStamps)
                        if (await StorageReferenceSources([stamp.Key])
                            .AnyAsync(source => source.ConcurrencyStamp == stamp.Value, token))
                            return true;
                    return false;
                }, cancellationToken);
            if (acceptAllChangesOnSuccess)
                ChangeTracker.AcceptAllChanges();
            return result;
        }
        catch
        {
            RestoreTrackedStorageStamps(stamps);
            throw;
        }
    }

    private async Task<int> PersistStorageReferencesAsync(
        List<(EntityEntry Entry, IProperty Property)> carriers, Func<Task<int>> persist,
        CancellationToken cancellationToken)
    {
        try
        {
            if (StorageReferenceTransactionFailed)
                throw StorageReferenceConflict();
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
            return await persist();
        }
        catch
        {
            PoisonStorageReferenceTransaction();
            throw;
        }
    }

    private (PropertyEntry Property, object? Original, object? Current, bool Modified)[] CaptureTrackedStorageStamps() =>
        ChangeTracker.Entries<StorageObject>()
            .Select(entry => entry.Property(nameof(StorageObject.ConcurrencyStamp)))
            .Select(property => (property, property.OriginalValue, property.CurrentValue, property.IsModified))
            .ToArray();

    private static void RestoreTrackedStorageStamps(
        (PropertyEntry Property, object? Original, object? Current, bool Modified)[] stamps)
    {
        foreach (var stamp in stamps)
        {
            stamp.Property.OriginalValue = stamp.Original;
            stamp.Property.CurrentValue = stamp.Current;
            stamp.Property.IsModified = stamp.Modified;
        }
    }

    private Dictionary<Guid, Guid> CaptureStorageCommitStamps()
    {
        // A surviving unique stamp proves the entire fence and owner write committed.
        // No matching stamp is not success: the provider must retry or surface failure.
        var stamps = _storageReferenceTransactionId == Database.CurrentTransaction?.TransactionId
            ? new Dictionary<Guid, Guid>(_fencedStorageReferences)
            : [];
        foreach (var entry in ChangeTracker.Entries<StorageObject>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
                stamps[entry.Entity.Id] = entry.Entity.ConcurrencyStamp;
            else if (entry.State == EntityState.Deleted)
                stamps.Remove(entry.Entity.Id);
        }
        return stamps;
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
            _fencedStorageReferences[source.Id] = stamp;
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
        if (_fencedStorageReferences.ContainsKey(source.Id))
            return false;
        var next = (source.TenantId, source.Id);
        if (_lastStorageReferenceFence is { } previous && next.CompareTo(previous) < 0)
            throw new InvalidOperationException("The full storage reference set must be fenced in tenant/object order.");
        _fencedStorageReferences.Add(source.Id, Guid.Empty);
        _lastStorageReferenceFence = next;
        return true;
    }

    private void RefreshStorageReferenceStamp(Guid objectId, Guid stamp)
    {
        foreach (var entry in ChangeTracker.Entries<StorageObject>()
                     .Where(entry => entry.Entity.Id == objectId).ToArray())
        {
            var property = entry.Property(item => item.ConcurrencyStamp);
            bool modified = property.IsModified;
            property.OriginalValue = stamp;
            property.CurrentValue = stamp;
            property.IsModified = modified;
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
