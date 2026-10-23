using System.Data;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Services;

internal enum EventInitialCreationWriteState { Before, After, Uncertain }

/// <summary>
/// Invocation-local evidence for the native CreateEvent callback, not a transaction or retry owner.
/// Initialize before storage fencing; capture once after the owning transaction's disclosure flush.
/// State reads require the caller's serializable transaction and the original tenant-bound context.
/// Unspecified source writes are uncertain; this is not evidence for an arbitrary callback's full graph.
/// </summary>
internal sealed class EventInitialCreationWriteReceipt : IDisposable
{
    private static readonly HashSet<Type> EventGraphTypes =
    [
        typeof(Event), typeof(EventParticipationConfiguration), typeof(EventRoleAssignment), typeof(EventIslamicAspect),
        typeof(EventDay), typeof(EventSession), typeof(EventLocation), typeof(EventAgendaItem),
        typeof(EventCategories), typeof(EventTags), typeof(EventCustomPropertyDefinition),
        typeof(EventCustomPropertyValue), typeof(EventCustomPropertyOption), typeof(EventCustomPropertyProjection),
        typeof(EventSessionIslamicAspect), typeof(EventSessionLanguage), typeof(EventSessionSpeaker),
        typeof(EventSessionCustomPropertyDefinition), typeof(EventSessionCustomPropertyValue),
        typeof(EventSessionCustomPropertyOption), typeof(EventSessionCustomPropertyProjection)
    ];

    private readonly ExploreDbContext _context;
    private readonly Guid _tenantId;
    private readonly Guid _eventId;
    private readonly Guid[] _imageIds;
    private readonly List<Row> _rows = [];
    private Image? _beforeDisclosure;
    private Image? _afterDisclosure;
    private Guid _writeTransactionId;
    private bool _initialized;
    private bool _captured;
    private bool _disposed;
    private bool _supported = true;
    private bool _completeAfter;

    internal EventInitialCreationWriteReceipt(ExploreDbContext context, Guid tenantId, Guid eventId, Guid[] imageIds)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(imageIds);
        if (tenantId == Guid.Empty || eventId == Guid.Empty || imageIds.Contains(Guid.Empty))
            throw new ArgumentException("Creation evidence requires exact non-empty resource identities.");
        _context = context;
        _tenantId = tenantId;
        _eventId = eventId;
        _imageIds = imageIds.Distinct().ToArray();
    }

    internal bool HasWrites { get; private set; }

    internal async Task InitializeAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RequireScope();
        if (_initialized || !HasEvidenceTransaction || _context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Creation evidence must start once in a clean caller-owned transaction.");

        var graph = await ReadEventGraphAsync(token);
        if (graph.Count != 0)
            throw new InvalidOperationException("Initial creation requires an absent Event and an empty source graph.");
        // A keyed database read also rejects an existing root hidden by tenant/soft-delete filters.
        var root = ImageOf(_context.Entry(new Event
        {
            Id = _eventId,
            Title = string.Empty,
            Actor = null!,
            Tenant = null!,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormat = null!
        }));
        if (await ReadRowAsync(root, token) is not null)
            throw new InvalidOperationException("Initial creation cannot reuse an existing Event identity.");
        _rows.Add(new(root, null));

        var images = await _context.StorageObjects.IgnoreQueryFilters([QueryFilterNames.SoftDelete]).AsNoTracking()
            .Where(row => row.TenantId == _tenantId && _imageIds.Contains(row.Id)).ToArrayAsync(token);
        if (images.Length != _imageIds.Length)
            throw new InvalidOperationException("Creation evidence requires every supplied storage object in its tenant.");
        foreach (var image in images)
        {
            var before = await ReadStoredAsync(_context.Entry(image), token)
                ?? throw new InvalidOperationException("A supplied storage object disappeared during initialization.");
            _rows.Add(new(before, before));
        }
        var recovery = await _context.Set<ModerationRecoveryStoreState>().AsNoTracking().SingleOrDefaultAsync(token);
        if (recovery is not null)
        {
            var before = await ReadStoredAsync(_context.Entry(recovery), token)
                ?? throw new InvalidOperationException("Canonical recovery state disappeared during initialization.");
            _rows.Add(new(before, before));
        }
        _beforeDisclosure = await ReadDisclosureAsync(token);
        _writeTransactionId = _context.Database.CurrentTransaction!.TransactionId;
        _initialized = true;
        _context.SavingChanges += ObserveSavingChanges;
    }

    internal async Task CaptureAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RequireScope();
        if (!_initialized || !HasEvidenceTransaction
            || _context.Database.CurrentTransaction!.TransactionId != _writeTransactionId || _context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Capture requires all source saves and the terminal disclosure flush.");
        if (_captured) return;

        var after = new Image?[_rows.Count];
        for (int index = 0; index < _rows.Count; index++)
            after[index] = await ReadRowAsync(_rows[index].Identity, token);
        var disclosure = await ReadDisclosureAsync(token);
        var graph = await ReadEventGraphAsync(token);
        for (int index = 0; index < _rows.Count; index++)
        {
            _rows[index].After = after[index];
            HasWrites |= !Same(_rows[index].Before, after[index]);
        }
        _afterDisclosure = disclosure;
        HasWrites |= !Same(_beforeDisclosure, disclosure);
        _completeAfter = _supported && CompleteEventGraph(graph)
            && _rows.All(row => row.Written
                ? row.Deleted == (row.After is null) : row.Before is null || row.After is not null)
            && disclosure is not null
            && disclosure.Values.GetValue<long>(nameof(EventDiscoveryRevision.DisclosureEpoch))
                == checked((_beforeDisclosure?.Values.GetValue<long>(nameof(EventDiscoveryRevision.DisclosureEpoch)) ?? 0) + 1)
            && disclosure.Values.GetValue<long>(nameof(EventDiscoveryRevision.IdentityEpoch))
                == (_beforeDisclosure?.Values.GetValue<long>(nameof(EventDiscoveryRevision.IdentityEpoch)) ?? 0);
        _captured = true;
    }

    internal async Task<EventInitialCreationWriteState> ReadStateAsync(CancellationToken token)
    {
        RequireScope();
        if (!_initialized || !_supported || !HasEvidenceTransaction)
            return EventInitialCreationWriteState.Uncertain;
        bool before = true;
        bool after = _captured && _completeAfter;
        foreach (var row in _rows)
        {
            var actual = await ReadRowAsync(row.Identity, token);
            before &= Same(actual, row.Before);
            after &= Same(actual, row.After);
        }
        var disclosure = await ReadDisclosureAsync(token);
        before &= Same(disclosure, _beforeDisclosure);
        after &= Same(disclosure, _afterDisclosure);
        var graph = await ReadEventGraphAsync(token);
        // Keyset comparison rejects later unobserved children, not just missing captured rows.
        before &= graph.Count == 0;
        after &= CompleteEventGraph(graph);
        if (after && !before) return EventInitialCreationWriteState.After;
        return before && !after ? EventInitialCreationWriteState.Before : EventInitialCreationWriteState.Uncertain;
    }

    private void ObserveSavingChanges(object? sender, SavingChangesEventArgs args)
    {
        RequireScope();
        var pending = _context.ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        if (pending.Length != 0 && _context.Database.CurrentTransaction?.TransactionId != _writeTransactionId)
            throw new InvalidOperationException("Creation source observation cannot cross its original native transaction.");
        if (_captured && pending.Length != 0)
            throw new InvalidOperationException("Source saves cannot follow the retained terminal creation image.");
        foreach (var entry in pending)
        {
            var key = entry.Metadata.FindPrimaryKey();
            if (key is null || key.Properties.Any(property => entry.Property(property.Name).IsTemporary))
                throw new InvalidOperationException("Creation evidence cannot retain temporary or keyless writes.");
            if (entry.State != EntityState.Added && key.Properties.Any(property =>
                    !Comparer(property).Equals(entry.OriginalValues[property], entry.CurrentValues[property])))
                throw new InvalidOperationException("Creation evidence cannot retain a changed primary key.");

            HasWrites = true;
            var identity = ImageOf(entry);
            var row = _rows.SingleOrDefault(candidate => SameKey(candidate.Identity, identity));
            if (row is null)
            {
                // Read the first physical before-image, not a later accepted save's OriginalValues.
                var stored = entry.GetDatabaseValues();
                var before = stored is null ? null : new Image(entry.Metadata, stored);
                if (entry.State == EntityState.Added && before is not null) _supported = false;
                row = new(identity, before);
                _rows.Add(row);
            }
            row.Written = true;
            row.Deleted = entry.State == EntityState.Deleted;
            if (!SupportedWrite(identity)) _supported = false;
        }
    }

    private bool SupportedWrite(Image image)
    {
        Type type = image.Type.ClrType;
        if (type == typeof(StorageObject))
            return _imageIds.Contains(image.Values.GetValue<Guid>(nameof(StorageObject.Id)));
        if (type == typeof(ModerationQualificationEpoch))
            return image.Values.GetValue<Guid>(nameof(ModerationQualificationEpoch.TenantId)) == _tenantId;
        if (type == typeof(ModerationRecoveryStoreState) || type == typeof(OutboxMessage)) return true;
        if (!EventGraphTypes.Contains(type)) return false;
        if (image.Type.FindProperty("TenantId") is not null && image.Values.GetValue<Guid>("TenantId") != _tenantId)
            return false;
        return type == typeof(Event) || type == typeof(EventParticipationConfiguration) || type == typeof(EventIslamicAspect)
            ? image.Values.GetValue<Guid>("Id") == _eventId
            : image.Type.FindProperty("EventId") is null || image.Values.GetValue<Guid>("EventId") == _eventId;
    }

    private bool CompleteEventGraph(List<Image> graph)
    {
        var expected = _rows.Where(row => EventGraphTypes.Contains(row.Identity.Type.ClrType)
            && row.Written && !row.Deleted).ToArray();
        if (graph.Count != expected.Length || graph.Any(actual =>
                !expected.Any(row => SameKey(row.Identity, actual) && Same(row.After, actual)))) return false;
        var root = graph.SingleOrDefault(row => row.Type.ClrType == typeof(Event));
        var participation = graph.SingleOrDefault(row => row.Type.ClrType == typeof(EventParticipationConfiguration));
        if (root is null || participation is null
            || root.Values.GetValue<bool>(nameof(Event.IsDeleted))
            || participation.Values.GetValue<bool>(nameof(EventParticipationConfiguration.IsDeleted))) return false;
        var owners = graph.Where(row => row.Type.ClrType == typeof(EventRoleAssignment)
            && row.Values.GetValue<int>(nameof(EventRoleAssignment.RoleId)) == (int)RoleEnum.EventOwner
            && row.Values.GetValue<EventRoleAssignmentStatus>(nameof(EventRoleAssignment.Status)) == EventRoleAssignmentStatus.Active).ToArray();
        return root.Values.GetValue<int>(nameof(Event.EventProvenanceTypeId)) == (int)EventProvenanceTypeEnum.CommunityReported
            ? owners.Length == 0 : owners.Length == 1;
    }

    private async Task<List<Image>> ReadEventGraphAsync(CancellationToken token)
    {
        List<Image> graph = [];
        Guid[] root = [_eventId];
        await ReadAsync<Event>("Id", root);
        await ReadAsync<EventParticipationConfiguration>("Id", root);
        await ReadAsync<EventRoleAssignment>("EventId", root);
        await ReadAsync<EventIslamicAspect>("Id", root);
        await ReadAsync<EventDay>("EventId", root);
        await ReadAsync<EventSession>("EventId", root);
        await ReadAsync<EventLocation>("EventId", root);
        await ReadAsync<EventAgendaItem>("EventId", root);
        await ReadAsync<EventCategories>("EventId", root);
        await ReadAsync<EventTags>("EventId", root);
        await ReadAsync<EventCustomPropertyDefinition>("EventId", root);
        await ReadAsync<EventCustomPropertyValue>("EventId", root);
        await ReadAsync<EventCustomPropertyProjection>("EventId", root);
        Guid[] sessions = Keys<EventSession>();
        await ReadAsync<EventSessionIslamicAspect>("EventSessionId", sessions);
        await ReadAsync<EventSessionLanguage>("EventSessionId", sessions);
        await ReadAsync<EventSessionSpeaker>("EventSessionId", sessions);
        await ReadAsync<EventSessionCustomPropertyDefinition>("EventSessionId", sessions);
        await ReadAsync<EventSessionCustomPropertyValue>("EventSessionId", sessions);
        await ReadAsync<EventSessionCustomPropertyProjection>("EventSessionId", sessions);
        await ReadAsync<EventCustomPropertyOption>("EventCustomPropertyDefinitionId", Keys<EventCustomPropertyDefinition>());
        await ReadAsync<EventSessionCustomPropertyOption>("EventSessionCustomPropertyDefinitionId", Keys<EventSessionCustomPropertyDefinition>());
        return graph;

        Guid[] Keys<TEntity>() => graph.Where(row => row.Type.ClrType == typeof(TEntity))
            .Select(row => row.Values.GetValue<Guid>("Id")).ToArray();

        async Task ReadAsync<TEntity>(string owner, Guid[] ids) where TEntity : class
        {
            if (ids.Length == 0) return;
            var entities = await _context.Set<TEntity>().IgnoreQueryFilters([QueryFilterNames.SoftDelete]).AsNoTracking()
                .Where(row => ids.Contains(EF.Property<Guid>(row, owner))).ToArrayAsync(token);
            foreach (var entity in entities)
                graph.Add(await ReadStoredAsync(_context.Entry(entity), token)
                    ?? throw new InvalidOperationException("Creation graph membership changed during its held read."));
        }
    }

    private async Task<Image?> ReadDisclosureAsync(CancellationToken token)
    {
        var revision = await _context.Set<EventDiscoveryRevision>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.TenantId == _tenantId, token);
        return revision is null ? null : await ReadStoredAsync(_context.Entry(revision), token);
    }

    private async Task<Image?> ReadRowAsync(Image identity, CancellationToken token)
    {
        // Detached probes retain only snapshotted scalars; they never attach or reload caller entities.
        var probe = _context.Entry(identity.Values.ToObject());
        foreach (var property in identity.Type.FindPrimaryKey()!.Properties)
            probe.Property(property.Name).CurrentValue = Comparer(property).Snapshot(identity.Values[property]);
        return await ReadStoredAsync(probe, token);
    }

    private static async Task<Image?> ReadStoredAsync(EntityEntry entry, CancellationToken token)
    {
        var actual = await entry.GetDatabaseValuesAsync(token);
        return actual is null ? null : new Image(entry.Metadata, actual);
    }

    private static Image ImageOf(EntityEntry entry) => new(entry.Metadata, entry.CurrentValues);

    private static bool SameKey(Image left, Image right) => left.Type == right.Type
        && left.Type.FindPrimaryKey()!.Properties.All(property => Comparer(property).Equals(left.Values[property], right.Values[property]));

    private static bool Same(Image? left, Image? right) => left is null ? right is null : right is not null
        && left.Type == right.Type && left.Values.Properties.All(property => Comparer(property).Equals(left.Values[property], right.Values[property]));

    private static ValueComparer Comparer(IProperty property) => property.GetValueComparer() ?? property.GetTypeMapping().Comparer;

    private bool HasEvidenceTransaction => _context.Database.CurrentTransaction is { } transaction
        && transaction.GetDbTransaction().IsolationLevel == IsolationLevel.Serializable;

    private void RequireScope()
    {
        if (_context.IsTenantFilterBypassed || _context.TenantFilterTenantId != _tenantId || !_context.Database.IsRelational())
            throw new InvalidOperationException("Creation evidence requires its original relational tenant authority.");
    }

    public void Dispose()
    {
        _context.SavingChanges -= ObserveSavingChanges;
        _disposed = true;
    }

    private sealed class Image
    {
        internal Image(IEntityType type, PropertyValues values)
        {
            Type = type;
            Values = values.Clone();
            foreach (var property in values.Properties)
                Values[property] = Comparer(property).Snapshot(values[property]);
        }

        internal IEntityType Type { get; }
        internal PropertyValues Values { get; }
    }

    private sealed class Row(Image identity, Image? before)
    {
        internal Image Identity { get; } = identity;
        internal Image? Before { get; } = before;
        internal Image? After { get; set; }
        internal bool Written { get; set; }
        internal bool Deleted { get; set; }
    }
}
