using Explore.Domain;
using Explore.Domain.Federation;
using Explore.Domain.Interfaces;
using Explore.Domain.Modules;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Explore.Persistence.Services;

/// <summary>
/// Accumulates source dependencies for the current native transaction. Source saves
/// never take an epoch lock; the transaction owner flushes after its final source write.
/// </summary>
internal sealed class EventDiscoveryDisclosureMutationScope(ExploreDbContext context)
{
    internal const int MaximumAffectedTenants = 1024;
    private const string Reason = TenantFilterBypassReasons.DiscoveryDisclosureMutation;
    private static readonly string[] OwnershipFields =
        ["TenantId", "ActorId", "OrganizerActorId", "SubmittedByUserId", "AtprotoRecordId"];
    private readonly HashSet<Guid> _tenants = [];
    private readonly HashSet<Guid> _tenantParents = [];
    private readonly HashSet<Guid> _deletedTenants = [];
    private readonly Dictionary<Guid, (long? Expected, long Advancement)> _identityRevisions = [];
    private readonly HashSet<Guid> _actors = [];
    private readonly HashSet<Guid> _users = [];
    private readonly HashSet<Guid> _records = [];
    private readonly HashSet<Guid> _newActors = [];
    private readonly HashSet<Guid> _newRecords = [];
    private readonly Dictionary<object, PropertyValues> _storedSources = new(ReferenceEqualityComparer.Instance);
    private Guid? _transactionId;
    private bool _terminal;
    private bool _flushed;
    private bool _finalizationFailed;
    private bool _tenantEnrollmentFenced;

    private static readonly HashSet<Type> TenantSources =
    [
        typeof(Event), typeof(EventSession), typeof(EventDay), typeof(EventLocation),
        typeof(Location), typeof(LocationRoom), typeof(EventAgendaItem), typeof(EventSessionAgendaItem),
        typeof(EventSessionGroup), typeof(EventSessionGroupSession), typeof(EventSessionSpeaker),
        typeof(EventSessionLanguage), typeof(EventSeries), typeof(EventPublicAction),
        typeof(EventCategories), typeof(EventTags), typeof(Category), typeof(Tag),
        typeof(CustomPropertyDefinition), typeof(CustomPropertyValue),
        typeof(EventCustomPropertyDefinition), typeof(EventCustomPropertyValue), typeof(EventCustomPropertyProjection),
        typeof(EventSessionCustomPropertyDefinition), typeof(EventSessionCustomPropertyValue),
        typeof(EventSessionCustomPropertyProjection), typeof(EventParticipationConfiguration),
        typeof(EventTicketCatalogVersion), typeof(EventTicketType), typeof(TicketTypeEntitlement),
        typeof(EventCapacityPool), typeof(TenantUser), typeof(OrganizationTenant), typeof(GroupTenant),
        typeof(StorageObject),
        typeof(ParticipationRequirementAttachment), typeof(RegistrationRequirement), typeof(RegistrationFormVersion),
        typeof(TenantCapability), typeof(AtprotoRecordTenantPresentation), typeof(AtprotoOutboundRecordOwnership)
    ];

    internal static bool IsDisclosureSetting(string key) =>
        key.StartsWith("public_experience.", StringComparison.Ordinal)
        || key.StartsWith("location_privacy.", StringComparison.Ordinal)
        || key.StartsWith("address_governance.", StringComparison.Ordinal)
        || key.StartsWith("federation.", StringComparison.Ordinal)
        || key.StartsWith("auth.", StringComparison.Ordinal)
        || key == "deployment.mode"
        || key == "custom_properties.projection_discovery_enabled";

    internal void Enlist(IEnumerable<Guid> tenantIds)
    {
        EnsureTransaction();
        if (_terminal)
            throw new InvalidOperationException("Source writes cannot follow the terminal discovery fence.");
        foreach (Guid tenantId in tenantIds)
        {
            if (tenantId == Guid.Empty)
                throw new InvalidOperationException("Disclosure mutations require an exact tenant.");
            if (_deletedTenants.Contains(tenantId))
                throw new InvalidOperationException("Discovery writes cannot follow physical tenant deletion.");
            _tenants.Add(tenantId);
            if (_tenants.Union(_identityRevisions.Keys).Count() > MaximumAffectedTenants)
                throw new InvalidOperationException("discovery_disclosure_fanout_exceeded");
        }
    }

    internal void EnlistIdentityRevision(Guid tenantId, long? expected = null, bool advance = false)
    {
        Enlist([]);
        if (tenantId == Guid.Empty)
            throw new InvalidOperationException("Discovery identity requires an exact tenant.");
        if (_deletedTenants.Contains(tenantId))
            throw new InvalidOperationException("Discovery writes cannot follow physical tenant deletion.");
        _identityRevisions.TryGetValue(tenantId, out var pending);
        if (expected is { } revision && pending.Expected is { } previous
            && revision != checked(previous + pending.Advancement))
            throw new InvalidOperationException("discovery_revision_conflict");
        _identityRevisions[tenantId] = (
            pending.Expected ?? expected,
            checked(pending.Advancement + (advance ? 1 : 0)));
        if (_tenants.Union(_identityRevisions.Keys).Count() > MaximumAffectedTenants)
            throw new InvalidOperationException("discovery_disclosure_fanout_exceeded");
    }

    internal async Task EnlistQueryAsync(IQueryable<Guid> tenantIds, CancellationToken cancellationToken)
    {
        Enlist([]);
        Guid[] ids = await tenantIds.Distinct().Take(MaximumAffectedTenants + 1).ToArrayAsync(cancellationToken);
        Enlist(ids);
    }

    internal void RecordTenantDeletion(Guid tenantId)
    {
        EnsureTransaction();
        // The repository records only a successful physical delete, within the
        // owning transaction. Its late flush must never recreate this Tenant FK.
        _deletedTenants.Add(tenantId);
        _tenants.Remove(tenantId);
        _identityRevisions.Remove(tenantId);
    }

    internal async Task EnlistAllTenantsAsync(CancellationToken cancellationToken)
    {
        await FenceTenantEnrollmentAsync(cancellationToken);
        await GloballyAsync(() => EnlistQueryAsync(
            context.Tenants.IgnoreAllFilters(Reason).Select(tenant => tenant.Id), cancellationToken), cancellationToken);
        Enlist(context.ChangeTracker.Entries<Tenant>().Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity.Id));
    }

    private async Task FenceTenantEnrollmentAsync(CancellationToken cancellationToken)
    {
        Enlist([]);
        if (_tenantEnrollmentFenced)
            return;
        await EventDiscoverySourceProviderOperations.FenceTenantEnrollmentAsync(context, cancellationToken);
        _tenantEnrollmentFenced = true;
    }

    internal async Task EnlistActorsAsync(IReadOnlyCollection<Guid> actorIds, CancellationToken cancellationToken)
    {
        EnsureTransaction();
        Guid[] ids = actorIds.Distinct().OrderBy(id => id.ToString("N"), StringComparer.Ordinal).ToArray();
        if (ids.Length == 0)
            return;
        if (_terminal)
            throw new InvalidOperationException("Source writes cannot follow the terminal discovery fence.");
        ids = ids.Where(id => !_actors.Contains(id) && !_newActors.Contains(id)).ToArray();
        if (ids.Length == 0)
            return;
        foreach (Guid id in ids)
            await RelationalEntityRowFence.AcquireGlobalAsync<Actor>(context, id, cancellationToken);
        await GloballyAsync(() => EnlistQueryAsync(
            context.Events.IgnoreAllFilters(Reason)
                .Where(value => ids.Contains(value.ActorId)
                    || value.OrganizerActorId != null && ids.Contains(value.OrganizerActorId.Value))
                .Select(value => value.TenantId)
                .Union(context.EventSessionSpeakers.IgnoreAllFilters(Reason)
                    .Where(value => ids.Contains(value.ActorId)).Select(value => value.TenantId))
                .Union(context.EventSeries.IgnoreAllFilters(Reason)
                    .Where(value => ids.Contains(value.ActorId)).Select(value => value.TenantId)),
            cancellationToken), cancellationToken);
        _actors.UnionWith(ids);
    }

    internal async Task EnlistUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        EnsureTransaction();
        if (_terminal)
            throw new InvalidOperationException("Source writes cannot follow the terminal discovery fence.");
        if (_users.Contains(userId))
            return;
        await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, userId, cancellationToken);
        Guid[] actors = await context.Actors.IgnoreAllFilters(Reason)
            .Where(actor => actor.UserId == userId).Select(actor => actor.Id).ToArrayAsync(cancellationToken);
        await EnlistActorsAsync(actors, cancellationToken);
        await GloballyAsync(() => EnlistQueryAsync(
            context.TenantUsers.IgnoreAllFilters(Reason).Where(value => value.UserId == userId)
                .Select(value => value.TenantId)
                .Union(context.Events.IgnoreAllFilters(Reason).Where(value => value.SubmittedByUserId == userId)
                    .Select(value => value.TenantId))
                .Union(context.StorageObjects.IgnoreAllFilters(Reason)
                    .Where(value => value.Actor != null && value.Actor.UserId == userId)
                    .Select(value => value.TenantId)), cancellationToken), cancellationToken);
        _users.Add(userId);
    }

    internal async Task EnlistRecordsAsync(IReadOnlyCollection<Guid> recordIds, CancellationToken cancellationToken)
    {
        EnsureTransaction();
        Guid[] ids = recordIds.Distinct().OrderBy(id => id.ToString("N"), StringComparer.Ordinal).ToArray();
        if (ids.Length == 0)
            return;
        if (_terminal)
            throw new InvalidOperationException("Source writes cannot follow the terminal discovery fence.");
        ids = ids.Where(id => !_records.Contains(id) && !_newRecords.Contains(id)).ToArray();
        if (ids.Length == 0)
            return;
        foreach (Guid id in ids)
            await RelationalEntityRowFence.AcquireGlobalAsync<AtprotoRecord>(context, id, cancellationToken);
        await GloballyAsync(() => EnlistQueryAsync(
            context.Events.IgnoreAllFilters(Reason)
                .Where(value => value.AtprotoRecordId != null && ids.Contains(value.AtprotoRecordId.Value))
                .Select(value => value.TenantId)
                .Union(context.AtprotoRecordTenantPresentations.IgnoreAllFilters(Reason)
                    .Where(value => ids.Contains(value.AtprotoRecordId)).Select(value => value.TenantId))
                .Union(context.AtprotoOutboundRecordOwnerships.IgnoreAllFilters(Reason)
                    .Where(value => ids.Contains(value.AtprotoRecordId)).Select(value => value.TenantId)),
            cancellationToken), cancellationToken);
        _records.UnionWith(ids);
    }

    internal async Task CaptureAsync(CancellationToken cancellationToken)
    {
        EnsureTransaction();
        _storedSources.Clear();
        EntityEntry[] pending = context.ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        if (pending.Any(entry => entry.Entity is ITenantEntity owned && _deletedTenants.Contains(owned.TenantId)
                || entry.Entity is Tenant tenant && _deletedTenants.Contains(tenant.Id)))
            throw new InvalidOperationException("Discovery writes cannot follow physical tenant deletion.");
        if (_terminal && pending.Any(entry =>
                entry.Entity is not EventDiscoverySnapshot and not EventDiscoverySnapshotItem))
            throw new InvalidOperationException("Source, audit and outbox writes cannot follow the terminal discovery fence.");
        if (_terminal)
            return;
        foreach (EntityEntry entry in pending)
        {
            if (entry.State == EntityState.Modified && entry.Entity is Event or EventSeries)
            {
                if (_terminal)
                    throw new InvalidOperationException("Source writes cannot follow the terminal discovery fence.");
                PropertyEntry[] writes = entry.Properties.Where(property => property.IsModified).ToArray();
                if (writes.Any(property => !CounterOrAudit(property))
                    && writes.All(property => CounterOrAudit(property)
                        || Same(property, property.CurrentValue, property.OriginalValue)))
                {
                    PropertyValues? stored = await ReadClassificationPlanAsync(entry, cancellationToken);
                    if (stored is not null)
                        _storedSources[entry.Entity] = stored;
                }
            }
        }
        EntityEntry[] entries = pending;
        _newActors.UnionWith(entries.Where(entry => entry.State == EntityState.Added && entry.Entity is Actor)
            .Select(entry => ((Actor)entry.Entity).Id));
        _newRecords.UnionWith(entries.Where(entry => entry.State == EntityState.Added && entry.Entity is AtprotoRecord)
            .Select(entry => ((AtprotoRecord)entry.Entity).Id));
        if (entries.Any(entry => entry.Entity is Tenant && entry.State == EntityState.Added))
            await FenceTenantEnrollmentAsync(cancellationToken);
        // Global anchors precede dependent source writes. Capture both original and
        // current relationship keys before deletes/EF AcceptAllChanges erase them.
        Guid[] users = entries.Where(entry => entry.Entity is User
                && (entry.State == EntityState.Deleted
                    || entry.State == EntityState.Modified && entry.Property(nameof(User.IsDeleted)).IsModified))
            .SelectMany(entry => Keys(entry, nameof(User.Id)))
            .Distinct().OrderBy(id => id.ToString("N"), StringComparer.Ordinal).ToArray();
        Guid[] actors = entries.Where(entry => entry.Entity is Actor or ActorPii or AtprotoIdentity)
            .SelectMany(entry => Keys(entry, entry.Entity is Actor ? nameof(Actor.Id) : nameof(ActorPii.ActorId)))
            .Distinct().ToArray();
        Guid[] records = entries.Where(entry => entry.Entity is AtprotoRecord or AtprotoEventProjection)
            .SelectMany(entry => Keys(entry, entry.Entity is AtprotoRecord
                ? nameof(AtprotoRecord.Id) : nameof(AtprotoEventProjection.AtprotoRecordId)))
            .Distinct().ToArray();

        EntityEntry[] owners = entries.Where(entry => TenantSources.Contains(entry.Metadata.ClrType)
            || entry.Entity is Actor or AtprotoIdentity).ToArray();
        EntityEntry[] classifications = entries.Where(entry => _storedSources.ContainsKey(entry.Entity)).ToArray();
        Guid[] enrollmentUsers = ChangedKeys(owners.Where(entry => entry.Entity is Actor or TenantUser), "UserId")
            .Concat(ChangedKeys(owners.Where(entry => entry.Entity is Event), nameof(Event.SubmittedByUserId)))
            .Concat(classifications.Where(entry => entry.Entity is Event)
                .SelectMany(entry => Keys(entry, nameof(Event.SubmittedByUserId))))
            .Concat(users).Distinct().ToArray();
        await FenceKeysAsync<User>(enrollmentUsers, cancellationToken);

        Guid[] organizations = ChangedKeys(owners, "OrganizationId")
            .Concat(entries.Where(entry => entry.Entity is Organization or OrganizationPii)
                .SelectMany(entry => Keys(entry, entry.Entity is Organization ? "Id" : "OrganizationId")))
            .Distinct().ToArray();
        Guid[] groups = ChangedKeys(owners, "GroupId")
            .Concat(entries.Where(entry => entry.Entity is Group).SelectMany(entry => Keys(entry, "Id")))
            .Distinct().ToArray();
        await FenceKeysAsync<Organization>(organizations, cancellationToken);
        await FenceKeysAsync<Group>(groups, cancellationToken);
        Guid[] relatedActors = users.Length + organizations.Length + groups.Length == 0 ? []
            : await context.Actors.IgnoreAllFilters(Reason)
                .Where(actor => actor.UserId != null && users.Contains(actor.UserId.Value)
                    || actor.OrganizationId != null && organizations.Contains(actor.OrganizationId.Value)
                    || actor.GroupId != null && groups.Contains(actor.GroupId.Value))
                .Select(actor => actor.Id).ToArrayAsync(cancellationToken);
        await FenceKeysAsync<Actor>(actors.Concat(relatedActors)
            .Concat(ChangedKeys(owners, "ActorId"))
            .Concat(ChangedKeys(owners, nameof(Event.OrganizerActorId)))
            .Concat(classifications.SelectMany(entry => Keys(entry, nameof(Event.ActorId))))
            .Concat(classifications.Where(entry => entry.Entity is Event)
                .SelectMany(entry => Keys(entry, nameof(Event.OrganizerActorId)))), cancellationToken);
        await FenceKeysAsync<AtprotoRecord>(records.Concat(ChangedKeys(owners, "AtprotoRecordId"))
            .Concat(classifications.Where(entry => entry.Entity is Event)
                .SelectMany(entry => Keys(entry, nameof(Event.AtprotoRecordId)))), cancellationToken);

        foreach (EntityEntry entry in entries)
        {
            switch (entry.Entity)
            {
                case Tenant tenant when entry.State != EntityState.Added:
                    Enlist([tenant.Id]);
                    break;
                case EventDiscoveryIdentity identity when entry.State == EntityState.Deleted
                    || entry.State == EntityState.Modified && entry.Property(nameof(EventDiscoveryIdentity.IsDeleted)).IsModified:
                    Enlist([identity.TenantId]);
                    break;
                case TenantSetting setting when IsDisclosureSetting(setting.SettingKey):
                    Enlist(Keys(entry, nameof(TenantSetting.TenantId)));
                    break;
                case SystemSetting setting when IsDisclosureSetting(setting.SettingKey):
                    await EnlistAllTenantsAsync(cancellationToken);
                    break;
                case ModuleDefinition when entry.State != EntityState.Added:
                    await EnlistAllTenantsAsync(cancellationToken);
                    break;
                case EventStatus or EventSessionStatus or EventType or EventFormat or VisibilityType
                    or AudienceAge or AudienceGender or ActorType or EventRegistrationPolicy
                    or Madhab or Language or EventProvenanceType or ParticipationHandlingMode
                    or AdvanceRegistrationObligation or IdentityAccessMode
                    or EventPublicActionKind or EventPublicActionHealthState or EventSessionKind or RegistrationMode
                    when entry.State != EntityState.Added:
                    await EnlistAllTenantsAsync(cancellationToken);
                    break;
            }
        }

        // An enrollment must touch its source anchor, not only take an FK shared
        // lock. Otherwise a PostgreSQL snapshot could omit a dependency inserted
        // after the snapshot but committed before the source mutator's fence.
        foreach (Guid user in users)
            await EnlistUserAsync(user, cancellationToken);
        await EnlistActorsAsync(actors.Concat(relatedActors).Distinct().ToArray(), cancellationToken);
        await EnlistRecordsAsync(records, cancellationToken);

        var ownerChecks = new List<Func<Task>>();
        foreach (EntityEntry entry in entries)
        {
            Guid[] keys;
            switch (entry.Entity)
            {
                case Organization or OrganizationPii when entry.State != EntityState.Added:
                    keys = Keys(entry, entry.Entity is Organization ? nameof(Organization.Id) : nameof(OrganizationPii.OrganizationId));
                    await EnlistActorsAsync(await context.Actors.IgnoreAllFilters(Reason)
                        .Where(value => value.OrganizationId != null && keys.Contains(value.OrganizationId.Value))
                        .Select(value => value.Id).ToArrayAsync(cancellationToken), cancellationToken);
                    break;
                case Group when entry.State != EntityState.Added:
                    keys = Keys(entry, nameof(Group.Id));
                    await EnlistActorsAsync(await context.Actors.IgnoreAllFilters(Reason)
                        .Where(value => value.GroupId != null && keys.Contains(value.GroupId.Value))
                        .Select(value => value.Id).ToArrayAsync(cancellationToken), cancellationToken);
                    break;
                case LocationPii:
                    keys = Keys(entry, nameof(LocationPii.LocationId));
                    await EnlistOwnersAsync<Location>(keys, ownerChecks, cancellationToken);
                    break;
                case EventIslamicAspect or EventTechAspect:
                    keys = Keys(entry, "Id");
                    await EnlistOwnersAsync<Event>(keys, ownerChecks, cancellationToken);
                    break;
                case EventSessionIslamicAspect:
                    keys = Keys(entry, nameof(EventSessionIslamicAspect.EventSessionId));
                    await EnlistOwnersAsync<EventSession>(keys, ownerChecks, cancellationToken);
                    break;
                case EventCustomPropertyOption:
                    keys = Keys(entry, nameof(EventCustomPropertyOption.EventCustomPropertyDefinitionId));
                    await EnlistOwnersAsync<EventCustomPropertyDefinition>(keys, ownerChecks, cancellationToken);
                    break;
                case EventSessionCustomPropertyOption:
                    keys = Keys(entry, nameof(EventSessionCustomPropertyOption.EventSessionCustomPropertyDefinitionId));
                    await EnlistOwnersAsync<EventSessionCustomPropertyDefinition>(keys, ownerChecks, cancellationToken);
                    break;
                case CustomPropertyOption:
                    keys = Keys(entry, nameof(CustomPropertyOption.CustomPropertyDefinitionId));
                    await EnlistOwnersAsync<CustomPropertyDefinition>(keys, ownerChecks, cancellationToken);
                    break;
            }
        }

        // Discover indirect owners and fanout before sorting the complete parent
        // set. User/Actor/record anchors precede Tenant, and Tenant precedes both
        // source-row classification and implicit source INSERT foreign-key locks.
        // Independent inserters must not both take shared Tenant FK locks and
        // then try to convert them to write locks at terminal finalization.
        await FenceTenantParentsAsync(_tenants
            .Concat(entries.Where(entry => entry.Entity is ITenantEntity
                    && TenantSources.Contains(entry.Metadata.ClrType))
                .SelectMany(entry => Keys(entry, nameof(ITenantEntity.TenantId)))), cancellationToken);

        foreach (var checkOwner in ownerChecks)
            await checkOwner();
        foreach (EntityEntry entry in entries)
        {
            if (entry.Entity is not ITenantEntity || !TenantSources.Contains(entry.Metadata.ClrType))
                continue;
            // Parent fencing is independent from epoch enrollment: a rank-only
            // classification still must not advance disclosure.
            if (entry.State == EntityState.Modified && entry.Entity is Event or EventSeries
                && await IsRankOnlyAsync(entry, cancellationToken))
                continue;
            Enlist(Keys(entry, nameof(ITenantEntity.TenantId)));
        }
    }

    private async Task FenceTenantParentsAsync(IEnumerable<Guid> tenantIds, CancellationToken cancellationToken)
    {
        foreach (Guid tenantId in tenantIds.Distinct()
                     .OrderBy(id => id.ToString("N"), StringComparer.Ordinal).ToArray())
        {
            if (_tenantParents.Contains(tenantId))
                continue;
            await EventDiscoveryDisclosureProviderOperations.InTenantAsync(context, tenantId, async () =>
            {
                await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, tenantId, cancellationToken);
                return true;
            }, cancellationToken);
            _tenantParents.Add(tenantId);
        }
    }

    private async Task EnlistOwnersAsync<TEntity>(
        Guid[] keys, List<Func<Task>> ownerChecks, CancellationToken cancellationToken)
        where TEntity : class, ITenantEntity
    {
        Enlist([]);
        var owners = await EventDiscoverySourceProviderOperations.ReadNonRetainingPlanAsync(
            context, ReadOwnersAsync, cancellationToken);
        if (keys.Length == 0 || keys.Any(key => !owners.ContainsKey(key)))
            throw new InvalidOperationException("discovery_disclosure_owner_unavailable");
        Enlist(owners.Values);
        ownerChecks.Add(async () =>
        {
            var held = await ReadOwnersAsync(context);
            if (keys.Any(key => !held.TryGetValue(key, out Guid tenantId) || tenantId != owners[key]))
                throw new DbUpdateConcurrencyException("Discovery source ownership changed before its tenant fence.");
        });

        async Task<Dictionary<Guid, Guid>> ReadOwnersAsync(ExploreDbContext reader)
        {
            var result = await reader.Set<TEntity>().AsNoTracking()
                .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                .Where(value => keys.Contains(EF.Property<Guid>(value, "Id")))
                .Select(value => new { Id = EF.Property<Guid>(value, "Id"), value.TenantId })
                .ToDictionaryAsync(value => value.Id, value => value.TenantId, cancellationToken);
            // New children can share a save with their new parent. Existing parents
            // must be visible to the current tenant/RLS authority, not inferred absent.
            foreach (var added in context.ChangeTracker.Entries<TEntity>()
                         .Where(value => value.State == EntityState.Added))
            {
                Guid id = (Guid)added.Property("Id").CurrentValue!;
                if (keys.Contains(id))
                    result[id] = added.Entity.TenantId;
            }
            return result;
        }
    }

    internal async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (!context.Database.IsRelational() || context.Database.CurrentTransaction is null)
            return;
        EnsureTransaction();
        if (_finalizationFailed)
            throw new InvalidOperationException("A failed disclosure finalization requires transaction rollback.");
        try
        {
            // Reservation bootstrap uses native SQL and has no Tenant FK. Even a
            // late bootstrap without a tracked snapshot must not resurrect it.
            foreach (Guid tenantId in _deletedTenants)
                if (await EventDiscoveryDisclosureRepository.InTenantAsync(context, tenantId,
                        () => context.Set<EventDiscoverySnapshotReservation>().IgnoreAllFilters(Reason)
                            .AnyAsync(row => row.TenantId == tenantId, cancellationToken), cancellationToken))
                    throw new InvalidOperationException("Discovery writes cannot follow physical tenant deletion.");
            if (_flushed || _tenants.Count == 0 && _identityRevisions.Count == 0 && _deletedTenants.Count == 0)
                return;
            await new EventDiscoveryDisclosureRepository(context)
                .FinalizeAsync(_tenants.ToArray(), _identityRevisions, cancellationToken);
            _flushed = true;
            _terminal = true;
        }
        catch
        {
            _finalizationFailed = true;
            throw;
        }
    }

    internal void Seal()
    {
        EnsureTransaction();
        _terminal = true;
    }

    internal void Reset()
    {
        _transactionId = null;
        _tenants.Clear();
        _tenantParents.Clear();
        _deletedTenants.Clear();
        _identityRevisions.Clear();
        _actors.Clear();
        _users.Clear();
        _records.Clear();
        _newActors.Clear();
        _newRecords.Clear();
        _storedSources.Clear();
        _terminal = false;
        _flushed = false;
        _finalizationFailed = false;
        _tenantEnrollmentFenced = false;
    }

    private void EnsureTransaction()
    {
        Guid id = context.Database.CurrentTransaction?.TransactionId
            ?? throw new InvalidOperationException("Disclosure source mutation requires a native transaction.");
        if (_transactionId == id)
            return;
        Reset();
        _transactionId = id;
    }

    private Guid[] Keys(EntityEntry entry, string property)
    {
        PropertyEntry value = entry.Property(property);
        object? stored = _storedSources.TryGetValue(entry.Entity, out PropertyValues? values)
            ? values[property] : null;
        return new[] { value.CurrentValue, entry.State == EntityState.Added ? null : value.OriginalValue, stored }
            .OfType<Guid>().Where(id => id != Guid.Empty).Distinct().ToArray();
    }

    private async Task<bool> IsRankOnlyAsync(EntityEntry entry, CancellationToken cancellationToken)
    {
        PropertyEntry[] writes = entry.Properties.Where(property => property.IsModified).ToArray();
        if (writes.Any(property => !CounterOrAudit(property)
                && !Same(property, property.CurrentValue, property.OriginalValue)))
            return false;
        if (writes.All(CounterOrAudit))
            return true;

        // GenericRepository.Update can attach a detached event and mark every
        // column modified. Compare its public fields against a held current row;
        // missing original values must not hide a title or ownership change.
        EventDiscoverySourceProviderOperations.RequireCurrentClassificationRead(context);
        Guid tenantId = ((ITenantEntity)entry.Entity).TenantId;
        Guid id = (Guid)entry.Property(nameof(Event.Id)).CurrentValue!;
        if (entry.Entity is Event)
            await RelationalEntityRowFence.AcquireAsync<Event>(context, tenantId, value => value.Id, id, cancellationToken);
        else
            await RelationalEntityRowFence.AcquireAsync<EventSeries>(context, tenantId, value => value.Id, id, cancellationToken);
        PropertyValues? stored = await entry.GetDatabaseValuesAsync(cancellationToken);
        if (stored is null)
            return false;
        if (!_storedSources.TryGetValue(entry.Entity, out PropertyValues? planned)
            || OwnershipFields
                .Where(name => entry.Metadata.FindProperty(name) is not null)
                .Any(name => !Equals(planned[name], stored[name])))
            throw new DbUpdateConcurrencyException("Discovery source ownership changed before its classification fence.");
        _storedSources[entry.Entity] = stored;
        if (stored[nameof(ITenantEntity.TenantId)] is Guid previousTenant && previousTenant != tenantId)
            Enlist([previousTenant]);
        return writes.All(property => CounterOrAudit(property)
            || Same(property, property.CurrentValue, stored[property.Metadata.Name]));
    }

    private Task<PropertyValues?> ReadClassificationPlanAsync(
        EntityEntry entry, CancellationToken cancellationToken) =>
        EventDiscoverySourceProviderOperations.ReadClassificationPlanAsync(context, entry, cancellationToken);

    private static bool CounterOrAudit(PropertyEntry property) => property.Metadata.Name is
        nameof(Event.TotalViews) or nameof(Event.UpdatedAt) or nameof(Event.UpdatedBy) or nameof(Event.ConcurrencyStamp);

    private static bool Same(PropertyEntry property, object? first, object? second) =>
        property.Metadata.GetValueComparer()?.Equals(first, second) ?? Equals(first, second);

    private IEnumerable<Guid> ChangedKeys(IEnumerable<EntityEntry> entries, string property) =>
        entries.Where(entry => entry.Metadata.FindProperty(property) is not null
                && (entry.State is EntityState.Added or EntityState.Deleted
                    || entry.Property(property).IsModified
                        && (_storedSources.TryGetValue(entry.Entity, out PropertyValues? stored)
                            ? !Equals(stored[property], entry.Property(property).CurrentValue)
                            : !Equals(entry.Property(property).OriginalValue, entry.Property(property).CurrentValue)
                                || entry.Properties.All(value => Equals(value.OriginalValue, value.CurrentValue)))))
            .SelectMany(entry => Keys(entry, property));

    private async Task FenceKeysAsync<TEntity>(IEnumerable<Guid> keys, CancellationToken cancellationToken)
        where TEntity : class
    {
        Guid[] ordered = keys.Distinct().OrderBy(id => id.ToString("N"), StringComparer.Ordinal).ToArray();
        if (ordered.Length > 0 && _terminal)
            throw new InvalidOperationException("Source writes cannot follow the terminal discovery fence.");
        foreach (Guid id in ordered)
            await RelationalEntityRowFence.AcquireGlobalAsync<TEntity>(context, id, cancellationToken);
    }

    private Task GloballyAsync(Func<Task> operation, CancellationToken cancellationToken) =>
        EventDiscoverySourceProviderOperations.GloballyAsync(context, operation, cancellationToken);
}
