using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Domain;
using Explore.Domain.Interfaces;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Kind = Explore.Domain.ModerationClassificationMaterialComponentKind;

namespace Explore.Persistence;

public partial class ExploreDbContext
{
    // This is the closed set of native material carriers, shared by inventory and stamping.
    // Registration answers, participants, UserPii and moderation decisions are not content
    // classification inputs. ActorPii carries only displayed actor text and profile media.
    // No caller can add a kind or assert that an omitted path is complete.
    private static readonly IReadOnlyDictionary<Type, Kind> ClassificationCarriers = new Dictionary<Type, Kind>
    {
        [typeof(Event)] = Kind.Event, [typeof(EventSession)] = Kind.Session, [typeof(EventResource)] = Kind.Resource,
        [typeof(EventResourceAudienceRule)] = Kind.ResourceAudience, [typeof(RegistrationForm)] = Kind.Form,
        [typeof(RegistrationFormVersion)] = Kind.FormVersion, [typeof(RegistrationFormSection)] = Kind.FormSection,
        [typeof(RegistrationFormField)] = Kind.FormField, [typeof(RegistrationFormFieldOption)] = Kind.FormOption,
        [typeof(RegistrationFormRule)] = Kind.FormRule, [typeof(RegistrationProviderBinding)] = Kind.ProviderBinding,
        [typeof(RegistrationProviderConnection)] = Kind.ProviderConnection, [typeof(RegistrationProviderApprovedOrigin)] = Kind.ProviderOrigin,
        [typeof(RegistrationProviderFieldMapping)] = Kind.ProviderField, [typeof(RegistrationProviderOptionMapping)] = Kind.ProviderOption,
        [typeof(RegistrationProviderCapability)] = Kind.ProviderCapability, [typeof(RegistrationProviderSchemaRevision)] = Kind.ProviderSchema,
        [typeof(RegistrationWorkflow)] = Kind.Workflow, [typeof(RegistrationRequirement)] = Kind.Requirement,
        [typeof(RegistrationChannel)] = Kind.Channel, [typeof(EventParticipationConfiguration)] = Kind.Participation,
        [typeof(ParticipationRequirementAttachment)] = Kind.Attachment, [typeof(EventCustomPropertyDefinition)] = Kind.EventProperty,
        [typeof(EventCustomPropertyOption)] = Kind.EventOption, [typeof(EventCustomPropertyValue)] = Kind.EventValue,
        [typeof(EventSessionCustomPropertyDefinition)] = Kind.SessionProperty, [typeof(EventSessionCustomPropertyOption)] = Kind.SessionOption,
        [typeof(EventSessionCustomPropertyValue)] = Kind.SessionValue, [typeof(EventPublicAction)] = Kind.PublicAction,
        [typeof(EventAgendaItem)] = Kind.Agenda, [typeof(EventDay)] = Kind.Day, [typeof(EventSessionGroup)] = Kind.SessionGroup,
        [typeof(EventSessionGroupSession)] = Kind.SessionGroupMember, [typeof(EventSessionSpeaker)] = Kind.Speaker,
        [typeof(StorageObject)] = Kind.Storage, [typeof(Actor)] = Kind.Actor,
        [typeof(EventIslamicAspect)] = Kind.EventIslamic, [typeof(EventTechAspect)] = Kind.EventTech,
        [typeof(EventSessionIslamicAspect)] = Kind.SessionIslamic, [typeof(EventLocation)] = Kind.Location,
        [typeof(ActorPii)] = Kind.ActorProfile
    };

    internal sealed record ClassificationNativeInventory(
        ImmutableArray<ModerationClassificationMaterial> Materials,
        ImmutableArray<ModerationClassificationMaterialComponentVersion> Components);

    internal async Task<ClassificationNativeInventory?> ReadClassificationInventoryAsync(
        Guid tenantId, Guid eventId, CancellationToken token)
    {
        if (TenantFilterTenantId != tenantId || IsTenantFilterBypassed || Database.CurrentTransaction is null) return null;
        var components = new List<ModerationClassificationMaterialComponentVersion>();
        var events = await Capture(Events.Where(row => row.Id == eventId && row.TenantId == tenantId && !row.IsDeleted));
        if (events.Count != 1 || events[0].ConcurrencyStamp == Guid.Empty) return null;
        var root = events[0];
        var sessions = await Capture(EventSessions.Where(row => row.EventId == eventId && row.TenantId == tenantId && !row.IsDeleted));
        var sessionIds = sessions.Select(row => row.Id).ToArray();
        var resources = await Capture(Set<EventResource>().Where(row => row.EventId == eventId && row.TenantId == tenantId && !row.IsDeleted));
        var resourceIds = resources.Select(row => row.Id).ToArray();
        if (sessions.Any(row => row.ConcurrencyStamp == Guid.Empty) || resources.Any(row => row.ConcurrencyStamp == Guid.Empty
            || row.EventSessionId is { } parent && !sessionIds.Contains(parent) || row.SessionScopeId != (row.EventSessionId ?? eventId)))
            return null;
        await Capture(Set<EventResourceAudienceRule>().Where(row => row.EventId == eventId && resourceIds.Contains(row.EventResourceId)));
        var forms = await Capture(Set<RegistrationForm>().Where(row => row.EventId == eventId && row.TenantId == tenantId));
        var formIds = forms.Select(row => row.Id).ToArray();
        var versions = await Capture(Set<RegistrationFormVersion>().Where(row => row.TenantId == tenantId
            && (row.EventId == eventId || formIds.Contains(row.RegistrationFormId))));
        var versionIds = versions.Select(row => row.Id).ToArray();
        var sections = await Capture(Set<RegistrationFormSection>().Where(row => row.TenantId == tenantId
            && (row.EventId == eventId || versionIds.Contains(row.RegistrationFormVersionId))));
        var sectionIds = sections.Select(row => row.Id).ToArray();
        var fields = await Capture(Set<RegistrationFormField>().Where(row => row.TenantId == tenantId
            && (row.EventId == eventId || sectionIds.Contains(row.RegistrationFormSectionId))));
        var fieldIds = fields.Select(row => row.Id).ToArray();
        var options = await Capture(Set<RegistrationFormFieldOption>().Where(row => row.TenantId == tenantId
            && (row.EventId == eventId || fieldIds.Contains(row.RegistrationFormFieldId))));
        var rules = await Capture(Set<RegistrationFormRule>().Where(row => row.TenantId == tenantId
            && (row.EventId == eventId || versionIds.Contains(row.RegistrationFormVersionId))));
        // Language and localized labels live on versions/sections/fields/options; schema and
        // mapping artifacts and conditions are included as persisted scalars, not just root stamps.
        if (versions.Any(row => row.EventId != eventId || !formIds.Contains(row.RegistrationFormId))
            || sections.Any(row => row.EventId != eventId || !versions.Any(parent => parent.Id == row.RegistrationFormVersionId && parent.RegistrationFormId == row.RegistrationFormId))
            || fields.Any(row => row.EventId != eventId || !sections.Any(parent => parent.Id == row.RegistrationFormSectionId
                && parent.RegistrationFormId == row.RegistrationFormId && parent.RegistrationFormVersionId == row.RegistrationFormVersionId))
            || options.Any(row => row.EventId != eventId || !fields.Any(parent => parent.Id == row.RegistrationFormFieldId
                && parent.RegistrationFormSectionId == row.RegistrationFormSectionId
                && parent.RegistrationFormId == row.RegistrationFormId && parent.RegistrationFormVersionId == row.RegistrationFormVersionId))
            || rules.Any(row => row.EventId != eventId || !versions.Any(parent => parent.Id == row.RegistrationFormVersionId && parent.RegistrationFormId == row.RegistrationFormId)))
            return null;
        await Capture(Set<RegistrationWorkflow>().Where(row => row.EventId == eventId && row.TenantId == tenantId));
        await Capture(Set<RegistrationRequirement>().Where(row => row.EventId == eventId && row.TenantId == tenantId));
        var channels = await Capture(Set<RegistrationChannel>().Where(row => row.EventId == eventId && row.TenantId == tenantId));
        await Capture(Set<EventParticipationConfiguration>().Where(row => row.Id == eventId && row.TenantId == tenantId));
        await Capture(Set<ParticipationRequirementAttachment>().Where(row => row.EventId == eventId && row.TenantId == tenantId));
        var channelBindings = channels.Where(row => row.RegistrationProviderBindingId != null).Select(row => row.RegistrationProviderBindingId!.Value).ToArray();
        var bindings = await Capture(Set<RegistrationProviderBinding>().Where(row => row.TenantId == tenantId
            && (formIds.Contains(row.RegistrationFormId) || channelBindings.Contains(row.Id))));
        if (bindings.Any(row => !versions.Any(parent => parent.Id == row.RegistrationFormVersionId && parent.RegistrationFormId == row.RegistrationFormId))
            || channelBindings.Any(id => bindings.All(row => row.Id != id))) return null;
        var bindingIds = bindings.Select(row => row.Id).ToArray();
        var connectionIds = bindings.Select(row => row.RegistrationProviderConnectionId)
            .Concat(versions.Where(row => row.ExternalRegistrationProviderConnectionId != null)
                .Select(row => row.ExternalRegistrationProviderConnectionId!.Value)).Distinct().ToArray();
        var connections = await Capture(Set<RegistrationProviderConnection>().Where(row => row.TenantId == tenantId && connectionIds.Contains(row.Id)));
        if (connections.Count != connectionIds.Length) return null;
        await Capture(Set<RegistrationProviderApprovedOrigin>().Where(row => row.TenantId == tenantId && connectionIds.Contains(row.RegistrationProviderConnectionId)));
        await Capture(Set<RegistrationProviderFieldMapping>().Where(row => row.TenantId == tenantId && bindingIds.Contains(row.RegistrationProviderBindingId)));
        await Capture(Set<RegistrationProviderOptionMapping>().Where(row => row.TenantId == tenantId && bindingIds.Contains(row.RegistrationProviderBindingId)));
        await Capture(Set<RegistrationProviderCapability>().Where(row => row.TenantId == tenantId && bindingIds.Contains(row.RegistrationProviderBindingId)));
        await Capture(Set<RegistrationProviderSchemaRevision>().Where(row => row.TenantId == tenantId && connectionIds.Contains(row.RegistrationProviderConnectionId)));
        var eventProperties = await Capture(Set<EventCustomPropertyDefinition>().Where(row => row.EventId == eventId && row.TenantId == tenantId));
        var eventPropertyIds = eventProperties.Select(row => row.Id).ToArray();
        await Capture(Set<EventCustomPropertyOption>().Where(row => eventPropertyIds.Contains(row.EventCustomPropertyDefinitionId)));
        await Capture(Set<EventCustomPropertyValue>().Where(row => row.EventId == eventId && row.TenantId == tenantId));
        var sessionProperties = await Capture(Set<EventSessionCustomPropertyDefinition>().Where(row => sessionIds.Contains(row.EventSessionId) && row.TenantId == tenantId));
        var sessionPropertyIds = sessionProperties.Select(row => row.Id).ToArray();
        await Capture(Set<EventSessionCustomPropertyOption>().Where(row => sessionPropertyIds.Contains(row.EventSessionCustomPropertyDefinitionId)));
        await Capture(Set<EventSessionCustomPropertyValue>().Where(row => sessionIds.Contains(row.EventSessionId) && row.TenantId == tenantId));
        await Capture(Set<EventPublicAction>().Where(row => row.EventId == eventId && row.TenantId == tenantId));
        await Capture(Set<EventAgendaItem>().Where(row => row.EventId == eventId && row.TenantId == tenantId));
        var days = await Capture(Set<EventDay>().Where(row => row.EventId == eventId && row.TenantId == tenantId));
        await Capture(Set<EventSessionGroup>().Where(row => row.EventId == eventId && row.TenantId == tenantId));
        await Capture(Set<EventSessionGroupSession>().Where(row => row.EventId == eventId && row.TenantId == tenantId));
        var speakers = await Capture(Set<EventSessionSpeaker>().Where(row => sessionIds.Contains(row.EventSessionId) && row.TenantId == tenantId));
        var actorIds = new[] { (Guid?)root.ActorId, root.OrganizerActorId }.OfType<Guid>()
            .Concat(speakers.Select(row => row.ActorId)).Distinct().ToArray();
        var actors = await Capture(Actors.Where(row => actorIds.Contains(row.Id) && !row.IsDeleted));
        if (actors.Count != actorIds.Length) return null;
        var actorProfiles = await Capture(ActorPii.Where(row => actorIds.Contains(row.ActorId)));
        await Capture(Set<EventIslamicAspect>().Where(row => row.Id == eventId));
        await Capture(Set<EventTechAspect>().Where(row => row.Id == eventId));
        await Capture(Set<EventSessionIslamicAspect>().Where(row => sessionIds.Contains(row.EventSessionId)));
        await Capture(Set<EventLocation>().Where(row => row.EventId == eventId && row.TenantId == tenantId));
        var storageIds = new[] { root.FeaturedImageId, root.BackgroundImageId }
            .Concat(sessions.Select(row => row.FeaturedImageId)).Concat(days.Select(row => row.BannerImageId))
            .Concat(actorProfiles.Select(row => row.ProfilePictureStorageObjectId))
            .Concat(resources.Select(row => row.StorageObjectId)).OfType<Guid>().Distinct().ToArray();
        var storage = await Capture(StorageObjects.Where(row => storageIds.Contains(row.Id) && row.TenantId == tenantId && !row.IsDeleted));
        if (storage.Count != storageIds.Length || components.Count > 4096) return null;
        var eventMaterial = new ModerationClassificationMaterial(eventId, root.ConcurrencyStamp, null, null, null, null);
        var materials = new List<ModerationClassificationMaterial> { eventMaterial };
        materials.AddRange(sessions.Select(row => eventMaterial with { SessionId = row.Id, SessionStamp = row.ConcurrencyStamp }));
        materials.AddRange(resources.Select(row => eventMaterial with
        {
            SessionId = row.EventSessionId, SessionStamp = sessions.SingleOrDefault(session => session.Id == row.EventSessionId)?.ConcurrencyStamp,
            ResourceId = row.Id, ResourceStamp = row.ConcurrencyStamp
        }));
        return materials.Count <= 4096 ? new(materials.ToImmutableArray(), components.ToImmutableArray()) : null;

        async Task<List<T>> Capture<T>(IQueryable<T> query) where T : class
        {
            if (components.Count > 4096) return [];
            var key = Model.FindEntityType(typeof(T))!.FindPrimaryKey()!.Properties.Single().Name;
            var bounded = query.AsNoTracking().OrderBy(row => EF.Property<Guid>(row, key)).Take(4097 - components.Count);
            var ids = await bounded.Select(row => EF.Property<Guid>(row, key)).ToArrayAsync(token);
            foreach (var id in ids) await RelationalEntityRowFence.AcquireGlobalAsync<T>(this, id, token);
            var rows = await bounded.ToListAsync(token);
            if (!ids.SequenceEqual(rows.Select(row => (Guid)Entry(row).Property(key).CurrentValue!)))
                throw new DbUpdateConcurrencyException("Native classification inventory changed during acquisition.");
            foreach (var row in rows)
                components.Add(ClassificationComponent(row, ClassificationCarriers[typeof(T)], key));
            return rows;
        }
    }

    private ModerationClassificationMaterialComponentVersion ClassificationComponent<T>(T row, Kind kind, string key) where T : class
    {
        var entry = Entry(row);
        // Persisted scalar equality also covers ExecuteUpdate/Delete writers. A concurrency
        // stamp alone cannot bind a changed label, mapped option, purpose or redirect.
        var values = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in entry.Metadata.GetProperties())
        {
            var value = entry.Property(property.Name).CurrentValue;
            var converter = property.GetTypeMapping().Converter;
            values.Add(property.Name, converter is null ? value : converter.ConvertToProvider(value));
        }
        var bytes = JsonSerializer.SerializeToUtf8Bytes(values);
        try
        {
            return new(kind, (Guid)entry.Property(key).CurrentValue!,
                row is IConcurrencyAware stamped ? stamped.ConcurrencyStamp : null,
                Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    /// <summary>
    /// Advances the real Event GUID stamp for tracked descendant mutations, including old
    /// and new ancestry. The scalar inventory remains mandatory for raw and unstamped paths.
    /// Runs before the ordinary concurrency loop so existing optimistic checks remain effective.
    /// </summary>
    private void PrepareClassificationMaterialStamps()
    {
        var changed = ChangeTracker.Entries().Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        if (changed.Any(entry => entry.State is EntityState.Modified or EntityState.Deleted
            && entry.Entity is ModerationClassificationControlSelection or ModerationClassificationControlBinding
                or ModerationClassificationMaterialSource or ModerationClassificationMaterialEntry
                or ModerationClassificationMaterialCategory or ModerationClassificationMaterialComponent)
            || changed.Any(entry => entry.State == EntityState.Deleted
                && entry.Entity is ModerationClassificationMaterialReview or ModerationClassificationEventTargetAdoption))
            throw new InvalidOperationException("Material classification provenance is immutable and cannot be deleted.");
        var eventIds = new HashSet<Guid>();
        var sessionIds = new HashSet<Guid>();
        var connectionIds = new HashSet<Guid>();
        var bindingIds = new HashSet<Guid>();
        var formIds = new HashSet<Guid>();
        foreach (var entry in changed.Where(entry => ClassificationCarriers.ContainsKey(entry.Metadata.ClrType)))
        {
            if (entry.Entity is Event or EventParticipationConfiguration or EventIslamicAspect or EventTechAspect)
                eventIds.UnionWith(Keys(entry, "Id"));
            else if (entry.Metadata.FindProperty("EventId") is not null) eventIds.UnionWith(Keys(entry, "EventId"));
            else if (entry.Metadata.FindProperty("EventSessionId") is not null) sessionIds.UnionWith(Keys(entry, "EventSessionId"));
            else if (entry.Entity is EventCustomPropertyOption)
            {
                var ids = Keys(entry, nameof(EventCustomPropertyOption.EventCustomPropertyDefinitionId));
                eventIds.UnionWith(Set<EventCustomPropertyDefinition>().Where(row => ids.Contains(row.Id)).Select(row => row.EventId));
                eventIds.UnionWith(ChangeTracker.Entries<EventCustomPropertyDefinition>().Where(row => ids.Contains(row.Entity.Id)).Select(row => row.Entity.EventId));
            }
            else if (entry.Entity is EventSessionCustomPropertyOption)
            {
                var ids = Keys(entry, nameof(EventSessionCustomPropertyOption.EventSessionCustomPropertyDefinitionId));
                sessionIds.UnionWith(Set<EventSessionCustomPropertyDefinition>().Where(row => ids.Contains(row.Id)).Select(row => row.EventSessionId));
                sessionIds.UnionWith(ChangeTracker.Entries<EventSessionCustomPropertyDefinition>().Where(row => ids.Contains(row.Entity.Id)).Select(row => row.Entity.EventSessionId));
            }
            else if (entry.Entity is RegistrationProviderConnection) connectionIds.UnionWith(Keys(entry, "Id"));
            else if (entry.Metadata.FindProperty("RegistrationProviderConnectionId") is not null)
            {
                connectionIds.UnionWith(Keys(entry, "RegistrationProviderConnectionId"));
                if (entry.Entity is RegistrationProviderBinding) formIds.UnionWith(Keys(entry, "RegistrationFormId"));
            }
            else if (entry.Metadata.FindProperty("RegistrationProviderBindingId") is not null)
                bindingIds.UnionWith(Keys(entry, "RegistrationProviderBindingId"));
            else if (entry.Entity is Actor or Explore.Domain.ActorPii)
            {
                var ids = Keys(entry, entry.Entity is Actor ? nameof(Actor.Id) : nameof(Explore.Domain.ActorPii.ActorId));
                eventIds.UnionWith(Events.Where(row => ids.Contains(row.ActorId) || row.OrganizerActorId != null
                    && ids.Contains(row.OrganizerActorId.Value)).Select(row => row.Id));
                sessionIds.UnionWith(Set<EventSessionSpeaker>().Where(row => ids.Contains(row.ActorId)).Select(row => row.EventSessionId));
            }
            else if (entry.Entity is StorageObject)
            {
                var ids = Keys(entry, "Id");
                eventIds.UnionWith(Events.Where(row => row.FeaturedImageId != null && ids.Contains(row.FeaturedImageId.Value)
                    || row.BackgroundImageId != null && ids.Contains(row.BackgroundImageId.Value)).Select(row => row.Id));
                eventIds.UnionWith(EventSessions.Where(row => row.FeaturedImageId != null && ids.Contains(row.FeaturedImageId.Value)).Select(row => row.EventId));
                eventIds.UnionWith(Set<EventDay>().Where(row => row.BannerImageId != null && ids.Contains(row.BannerImageId.Value)).Select(row => row.EventId));
                eventIds.UnionWith(Set<EventResource>().Where(row => row.StorageObjectId != null && ids.Contains(row.StorageObjectId.Value)).Select(row => row.EventId));
            }
        }
        if (bindingIds.Count != 0 || connectionIds.Count != 0)
        {
            formIds.UnionWith(Set<RegistrationProviderBinding>().Where(row => bindingIds.Contains(row.Id)
                || connectionIds.Contains(row.RegistrationProviderConnectionId)).Select(row => row.RegistrationFormId));
            formIds.UnionWith(ChangeTracker.Entries<RegistrationProviderBinding>().Where(row => bindingIds.Contains(row.Entity.Id)
                || connectionIds.Contains(row.Entity.RegistrationProviderConnectionId)).Select(row => row.Entity.RegistrationFormId));
            eventIds.UnionWith(Set<RegistrationFormVersion>().Where(row => row.ExternalRegistrationProviderConnectionId != null
                && connectionIds.Contains(row.ExternalRegistrationProviderConnectionId.Value)).Select(row => row.EventId));
        }
        if (formIds.Count != 0)
        {
            eventIds.UnionWith(Set<RegistrationForm>().Where(row => formIds.Contains(row.Id)).Select(row => row.EventId));
            eventIds.UnionWith(ChangeTracker.Entries<RegistrationForm>().Where(row => formIds.Contains(row.Entity.Id)).Select(row => row.Entity.EventId));
        }
        if (sessionIds.Count != 0)
        {
            eventIds.UnionWith(EventSessions.Where(row => sessionIds.Contains(row.Id)).Select(row => row.EventId));
            foreach (var entry in ChangeTracker.Entries<EventSession>().Where(row => sessionIds.Contains(row.Entity.Id)))
                eventIds.UnionWith(Keys(entry, nameof(EventSession.EventId)));
        }
        foreach (var id in eventIds.Order())
        {
            var tracked = ChangeTracker.Entries<Event>().SingleOrDefault(entry => entry.Entity.Id == id);
            var entity = tracked?.Entity ?? Events.SingleOrDefault(row => row.Id == id);
            if (entity is null || tracked?.State is EntityState.Added or EntityState.Deleted) continue;
            entity.ConcurrencyStamp = Guid.CreateVersion7();
        }

        static Guid[] Keys(EntityEntry entry, string name) =>
            new[] { entry.Property(name).OriginalValue, entry.Property(name).CurrentValue }.OfType<Guid>()
                .Where(id => id != Guid.Empty).Distinct().ToArray();
    }
}
