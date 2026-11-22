using System.Collections.Immutable;
using System.Data.Common;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Rejects a selected operation's EF write intent outside its original observation. Tracked values
/// are used only to reject writes, never as current source evidence or final admission. Physical
/// source reservations exclude other writers; the lease still rereads persisted rows after flush.
/// </summary>
internal sealed class HumanEventProviderSourceWriteGuard(
    ExploreDbContext context, DbTransaction transaction, HumanEventProviderSourceFacts observed,
    IReadOnlyCollection<Event> originalEvents)
{
    internal static SaveChangesInterceptor SaveInterceptor { get; } = new PreparedWriteInterceptor();

    private readonly Dictionary<Guid, PropertyValues> _events = originalEvents.ToDictionary(
        root => root.Id, root => context.Entry(root).CurrentValues.Clone());
    private readonly Dictionary<Guid, Guid> _nativeStamps = [];
    private readonly Dictionary<Guid, PropertyValues> _pendingEvents = [];
    private bool _saving;
    private bool _invalid;
    private HashSet<Type> _classificationCarriers = [];
    private readonly HashSet<Guid> _createdBindings = [];
    private readonly HashSet<(HumanEventProviderSource Source, Guid Id)> _createdRows = [];
    private readonly HashSet<Guid> _pendingBindings = [];
    private readonly HashSet<(HumanEventProviderSource Source, Guid Id)> _pendingRows = [];
    private readonly HashSet<(HumanEventProviderSource Source, Guid Id)> _pendingDeletes = [];
    private readonly DbContextId _contextId = context.ContextId;

    internal ImmutableArray<(HumanEventProviderSource Source, Guid Id)> CreatedRows => [.. _createdRows];

    internal void Attach()
    {
        if (context.HumanEventProviderWrites is not null) throw Rejected();
        context.HumanEventProviderWrites = this;
        context.SavedChanges += OnSavedChanges;
        context.SaveChangesFailed += OnSaveChangesFailed;
    }

    internal void Detach()
    {
        _invalid = true;
        _events.Clear();
        _nativeStamps.Clear();
        _pendingEvents.Clear();
        _createdBindings.Clear();
        _createdRows.Clear();
        ClearPending();
        if (ReferenceEquals(context.HumanEventProviderWrites, this))
            context.HumanEventProviderWrites = null;
        context.SavedChanges -= OnSavedChanges;
        context.SaveChangesFailed -= OnSaveChangesFailed;
    }

    internal void BeginSave(EntityEntry[] changed, IEnumerable<Type> classificationCarriers)
    {
        RequireOwner();
        if (_saving) throw Rejected();
        _saving = true;
        _classificationCarriers = classificationCarriers.ToHashSet();
        foreach (var entry in changed)
        {
            if (entry.Entity is Event root)
            {
                if (entry.State is EntityState.Added or EntityState.Deleted) throw Rejected();
                RequireImage(entry.CurrentValues, EventImage(root), includeAudit: true);
            }
            else if (_classificationCarriers.Contains(entry.Metadata.ClrType)
                && !HumanEventProviderSourceSql.Sources.Any(source => source.Type == entry.Metadata.ClrType))
                throw Rejected();
        }
    }

    internal void RecordClassificationStamp(Event root, Guid stamp)
    {
        RequireOwner();
        if (!_saving || _nativeStamps.ContainsKey(root.Id)) throw Rejected();
        RequireImage(context.Entry(root).CurrentValues, EventImage(root), includeAudit: true);
        _nativeStamps.Add(root.Id, stamp);
    }

    internal void RecordPreparedStamp(Event root, Guid stamp)
    {
        RequireOwner();
        if (!_saving || !_nativeStamps.TryGetValue(root.Id, out Guid predecessor)
            || root.ConcurrencyStamp != predecessor) throw Rejected();
        _nativeStamps[root.Id] = stamp;
    }

    internal void StagePreparedEvents()
    {
        RequireOwner();
        foreach (var (id, stamp) in _nativeStamps)
        {
            var entry = context.ChangeTracker.Entries<Event>().Single(row => row.Entity.Id == id);
            RequireImage(entry.CurrentValues, _events[id], includeAudit: false);
            if (entry.Entity.ConcurrencyStamp != stamp) throw Rejected();
            _pendingEvents.Add(id, entry.CurrentValues.Clone());
        }
    }

    internal async Task<Guid> ValidateEventsAsync(CancellationToken token)
    {
        RequireOwner();
        if (_saving) throw Rejected();
        foreach (var (id, expected) in _events)
        {
            var root = await context.Events.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token)
                ?? throw Rejected();
            if (root.ConcurrencyStamp != (Guid)expected[nameof(Event.ConcurrencyStamp)]!)
                throw new UnauthorizedAccessException("The original Event revision is no longer current.");
            RequireImage(context.Entry(root).CurrentValues, expected, includeAudit: true);
        }
        RequireOwner();
        return (Guid)_events[observed.Target.EventId][nameof(Event.ConcurrencyStamp)]!;
    }

    private PropertyValues EventImage(Event root)
    {
        return _events.TryGetValue(root.Id, out var image) ? image : throw Rejected();
    }

    private void RequireOwner()
    {
        if (_invalid || context.ContextId != _contextId
            || !ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), transaction))
            throw Rejected();
    }

    private static void RequireImage(PropertyValues current, PropertyValues expected, bool includeAudit)
    {
        foreach (var property in expected.Properties)
        {
            if (!includeAudit && property.Name is nameof(Event.ConcurrencyStamp)
                or nameof(Event.UpdatedAt) or nameof(Event.UpdatedBy)) continue;
            if (!property.GetValueComparer().Equals(current[property], expected[property])) throw Rejected();
        }
    }

    private void ClearPending()
    {
        _pendingBindings.Clear();
        _pendingRows.Clear();
        _pendingDeletes.Clear();
    }

    private void OnSavedChanges(object? sender, SavedChangesEventArgs args)
    {
        RequireOwner();
        foreach (var (id, image) in _pendingEvents) _events[id] = image;
        _pendingEvents.Clear();
        _nativeStamps.Clear();
        _saving = false;
        _createdBindings.UnionWith(_pendingBindings);
        _createdRows.UnionWith(_pendingRows);
        _createdRows.ExceptWith(_pendingDeletes);
        foreach (var row in _pendingDeletes.Where(row => row.Source == HumanEventProviderSource.Binding))
            _createdBindings.Remove(row.Id);
        ClearPending();
    }

    private void OnSaveChangesFailed(object? sender, SaveChangesFailedEventArgs args)
    {
        _invalid = true;
        _pendingEvents.Clear();
        _nativeStamps.Clear();
        ClearPending();
    }

    private void ValidateSaving()
    {
        RequireOwner();
        if (!_saving) throw Rejected();
        foreach (var (id, expected) in _pendingEvents)
        {
            var entry = context.ChangeTracker.Entries<Event>().Single(row => row.Entity.Id == id);
            RequireImage(entry.CurrentValues, expected, includeAudit: true);
        }
        ClearPending();
        context.ChangeTracker.DetectChanges();
        var target = observed.Target;
        var entries = context.ChangeTracker.Entries().Where(entry =>
            entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        foreach (var entry in entries)
        {
            if (entry.Entity is Event root)
            {
                if (entry.State != EntityState.Modified
                    || !_pendingEvents.TryGetValue(root.Id, out var expected))
                    throw Rejected();
                RequireImage(entry.CurrentValues, expected, includeAudit: true);
                continue;
            }
            var source = HumanEventProviderSourceSql.Sources.SingleOrDefault(source => source.Type == entry.Metadata.ClrType);
            if (source.Type is null)
            {
                if (_classificationCarriers.Contains(entry.Metadata.ClrType)) throw Rejected();
                continue;
            }
            Guid id = (Guid)entry.Property("Id").CurrentValue!;
            string tenantProperty = source.Source == HumanEventProviderSource.SecretBinding ? "ScopeId" : "TenantId";
            if (entry.Property(tenantProperty).CurrentValue is not Guid tenant || tenant != target.TenantId)
                throw Rejected();
            if (entry.State != EntityState.Added)
            {
                if (!observed.Rows.Any(row => row.Source == source.Source && row.Id == id)
                    && !_createdRows.Contains((source.Source, id)))
                    throw Rejected();
                if (entry.State == EntityState.Deleted)
                    _pendingDeletes.Add((source.Source, id));
                continue;
            }
            if (source.Source == HumanEventProviderSource.Binding)
            {
                if (target.Operation != HumanEventProviderOperation.CreateBinding
                    || (Guid)entry.Property("RegistrationFormId").CurrentValue! != target.FormId
                    || (Guid)entry.Property("RegistrationFormVersionId").CurrentValue! != target.FormVersionId
                    || (Guid)entry.Property("RegistrationProviderConnectionId").CurrentValue! != target.ConnectionId)
                    throw Rejected();
                _pendingBindings.Add(id);
            }
        }
        foreach (var entry in entries.Where(entry => entry.State == EntityState.Added))
        {
            var source = HumanEventProviderSourceSql.Sources.SingleOrDefault(source => source.Type == entry.Metadata.ClrType);
            if (source.Type is null) continue;
            if (entry.Metadata.FindProperty("EventId") is not null
                && entry.Property("EventId").CurrentValue is Guid eventId && eventId != target.EventId)
                throw Rejected();
            if (entry.Metadata.FindProperty("RegistrationProviderBindingId") is not null)
            {
                Guid? bindingId = entry.Property("RegistrationProviderBindingId").CurrentValue as Guid?;
                if (bindingId != target.BindingId && (!bindingId.HasValue
                    || !_createdBindings.Contains(bindingId.Value) && !_pendingBindings.Contains(bindingId.Value)))
                    throw Rejected();
            }
            if (source.Source == HumanEventProviderSource.Schema
                && (target.Operation != HumanEventProviderOperation.ImportVersion
                    || entry.Property("RegistrationProviderConnectionId").CurrentValue as Guid? != target.ConnectionId))
                throw Rejected();
            if (source.Source is HumanEventProviderSource.Message or HumanEventProviderSource.Effect)
            {
                string property = source.Source == HumanEventProviderSource.Message ? "ProviderMessageId" : "ProviderDecisionId";
                if (target.Operation != HumanEventProviderOperation.ManualImport || target.BindingId is null
                    || entry.Property(property).CurrentValue is not string decision
                    || !decision.StartsWith(target.BindingId.Value.ToString("N") + ":", StringComparison.Ordinal))
                    throw Rejected();
            }
            if (source.Source is HumanEventProviderSource.Connection or HumanEventProviderSource.ApprovedOrigin
                or HumanEventProviderSource.Receipt or HumanEventProviderSource.SecretBinding
                or HumanEventProviderSource.Workflow or HumanEventProviderSource.Requirement or HumanEventProviderSource.Submission)
                throw Rejected();
            _pendingRows.Add((source.Source, (Guid)entry.Property("Id").CurrentValue!));
        }
    }

    private static InvalidOperationException Rejected() => new("human_event_provider_write_outside_original_source");

    private sealed class PreparedWriteInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData, InterceptionResult<int> result)
        {
            (eventData.Context as ExploreDbContext)?.HumanEventProviderWrites?.ValidateSaving();
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            (eventData.Context as ExploreDbContext)?.HumanEventProviderWrites?.ValidateSaving();
            return ValueTask.FromResult(result);
        }
    }
}
