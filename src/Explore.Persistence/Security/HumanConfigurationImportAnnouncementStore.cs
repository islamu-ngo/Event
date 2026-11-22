using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Announcements;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Security;

/// <summary>
/// Announcement intent, excluded originals and canonical rows on the actual import owner.
/// Neither a public store implementation nor an effect array can create this private completion.
/// </summary>
internal sealed class HumanConfigurationImportAnnouncementStore : IHumanConfigurationImportAnnouncementStore
{
    private const string Key = GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication;
    private readonly ExploreDbContext context;
    private readonly ExploreDbContextExecutionLease lease;
    private readonly IHumanConfigurationImportAdmission issuer;
    private readonly HumanConfigurationImportAuthority authority;
    private readonly Guid operationId;
    private readonly string digest;
    private readonly ImmutableArray<AnnouncementImportIntent> intents;
    private readonly DateTime occurredAt;
    private readonly DbContextId contextId;
    private readonly RelationalConnectionIdentity connection;
    private readonly DbTransaction transaction;
    private readonly string tables;
    private readonly PostgresNativeQueueEnrollment? enrollment;
    private Dictionary<AnnouncementOwner, JsonObject?> expected = [];
    private ImmutableArray<Plan> plans;
    private readonly HashSet<HumanConfigurationImportAnnouncement> completed = [];
    private bool failed;
    private bool writing;

    private HumanConfigurationImportAnnouncementStore(ExploreDbContext context, ExploreDbContextExecutionLease lease,
        IServiceProvider services, HumanConfigurationImportAuthority authority, Guid operationId, string digest,
        ImmutableArray<AnnouncementImportIntent> intents, DateTime occurredAt)
    {
        this.context = context;
        this.lease = lease;
        this.authority = authority;
        this.operationId = operationId;
        this.digest = digest;
        this.intents = intents;
        issuer = services.GetRequiredService<IHumanConfigurationImportAdmission>();
        contextId = context.ContextId;
        connection = RelationalConnectionIdentity.Capture(context);
        transaction = context.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new UnauthorizedAccessException("Announcement import requires its acquired original transaction.");
        this.occurredAt = context.Database.IsNpgsql()
            ? new DateTime(occurredAt.Ticks - occurredAt.Ticks % 10, DateTimeKind.Utc) : occurredAt;
        tables = Table(typeof(SystemSetting)) + "\n" + Table(typeof(TenantSetting));
        enrollment = lease.HumanScope?.Enrollment;
    }

    internal static async Task<HumanConfigurationImportAnnouncementStore> ObserveAsync(
        ExploreDbContext context, ExploreDbContextExecutionLease lease, IServiceProvider services,
        HumanConfigurationImportAuthority authority, Guid operationId, string digest,
        ImmutableArray<AnnouncementImportIntent> intents, DateTime occurredAt, CancellationToken token)
    {
        if (!ReferenceEquals(context, services.GetRequiredService<ExploreDbContext>())
            || operationId == Guid.Empty || string.IsNullOrWhiteSpace(digest)
            || occurredAt == default || occurredAt.Kind != DateTimeKind.Utc || intents.IsDefaultOrEmpty
            || intents.Any(intent => intent is null || intent.Owner is null || intent.Change is null
                || authority.Target.TenantId.HasValue && intent.Owner.TenantId != authority.Target.TenantId)
            || intents.Select(intent => intent.Owner).Distinct().Count() != intents.Length)
            throw new UnauthorizedAccessException("Announcement import observation requires the original parsed target intents.");
        HumanSourceProviderOperations.RequirePostgresOrSqlite(context, "announcement_import_provider_not_supported");
        var store = new HumanConfigurationImportAnnouncementStore(
            context, lease, services, authority, operationId, digest, intents, occurredAt);
        store.RequireSelection();
        store.expected = await store.ReadImageAsync(token);
        store.RequireSelection();
        return store;
    }

    public async Task RequireCurrentAsync(CancellationToken cancellationToken)
    {
        try
        {
            RequireSelection();
            if (writing)
                throw new UnauthorizedAccessException("An announcement import write has not privately completed.");
            var actual = await ReadImageAsync(cancellationToken);
            if (actual.Count != expected.Count || expected.Any(pair =>
                !actual.TryGetValue(pair.Key, out var row) || !JsonNode.DeepEquals(pair.Value, row)))
                throw new UnauthorizedAccessException("Original announcement import rows, metadata or absence changed.");
            RequireSelection();
        }
        catch { failed = true; throw; }
    }

    internal void RequireFactory(ExploreDbContextExecutionLease originalLease,
        ExploreDbContext originalContext, IHumanConfigurationImportAdmission originalIssuer)
    {
        if (!ReferenceEquals(lease, originalLease) || !ReferenceEquals(context, originalContext)
            || !ReferenceEquals(issuer, originalIssuer))
        {
            failed = true;
            throw new UnauthorizedAccessException("An announcement store cannot move to another import factory.");
        }
        RequireSelection();
    }

    public async Task<ImmutableArray<HumanConfigurationImportSettingEffect>> CompileAsync(
        HumanConfigurationImportApply original, ImmutableArray<HumanConfigurationImportAnnouncement> announcements,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!plans.IsDefault || original.OperationId != operationId || original.ArtifactDigest != digest
                || original.Target != authority.Target || !ReferenceEquals(original.Subject, authority.Subject)
                || !ReferenceEquals(original.AnnouncementStore, this) || announcements.Length != intents.Length
                || !announcements.SequenceEqual(original.Announcements))
                throw new UnauthorizedAccessException("Only the accepted original import can compile its announcement slice once.");
            await RequireCurrentAsync(cancellationToken);
            var built = ImmutableArray.CreateBuilder<Plan>();
            var effects = ImmutableArray.CreateBuilder<HumanConfigurationImportSettingEffect>();
            for (int index = 0; index < announcements.Length; index++)
            {
                var slice = announcements[index];
                RequireSlice(slice);
                if (slice.Intent != intents[index])
                    throw new UnauthorizedAccessException("Announcement compilation substituted the parsed original intent.");
                await issuer.RequireAnnouncementAsync(slice, cancellationToken);
                JsonObject? before = expected[slice.Intent.Owner];
                var current = Source(slice.Intent.Owner, before);
                bool changed = AnnouncementPublicationPolicy.RequiresSelectionEpoch(
                    current.Group, current.IsLocked, slice.Intent.Change);
                if (changed && authority.Target.TenantId.HasValue
                    && (current.IsLocked || Source(AnnouncementOwner.Instance, expected[AnnouncementOwner.Instance]).IsLocked))
                    throw new UnauthorizedAccessException("The original announcement import source is locked.");
                Guid? edition = AnnouncementPublicationPolicy.RequiresMaterialEdition(current.Group, slice.Intent.Change)
                    ? Guid.NewGuid() : null;
                var write = AnnouncementPublicationPolicy.Apply(current.Owner, current.Group, current.IsLocked,
                    slice.Intent.Change, edition, changed ? Guid.NewGuid() : null);
                var sliceEffects = ImmutableArray.CreateBuilder<HumanConfigurationImportSettingEffect>();
                JsonObject? after = before?.DeepClone().AsObject();
                if (changed)
                {
                    string value = AnnouncementSettingValueCodec.SerializeGroup(write.Group
                        ?? throw new InvalidOperationException("Announcement import must retain its complete group."));
                    after = BuildAfter(slice.Intent.Owner, before, value, write.IsLocked);
                    SettingScope scope = slice.Intent.Owner.TenantId.HasValue ? SettingScope.Tenant : SettingScope.Instance;
                    sliceEffects.Add(new(scope, slice.Intent.Owner.TenantId, Key, HumanSettingsMutationKind.SetValue, value));
                    if (current.IsLocked != write.IsLocked)
                        sliceEffects.Add(new(scope, slice.Intent.Owner.TenantId, Key, HumanSettingsMutationKind.SetLock,
                            IsLocked: write.IsLocked));
                }
                var derived = sliceEffects.ToImmutable();
                effects.AddRange(derived);
                built.Add(new(slice, before?.DeepClone().AsObject(), after, derived));
            }
            plans = built.ToImmutable();
            return effects.ToImmutable();
        }
        catch { failed = true; throw; }
    }

    public async Task<ImmutableArray<SettingChangedNotification>> ApplyAsync(
        HumanConfigurationImportAnnouncement announcement, CancellationToken cancellationToken)
    {
        try
        {
            RequireSlice(announcement);
            if (plans.IsDefault || !completed.Add(announcement))
                throw new UnauthorizedAccessException("Announcement import requires an unconsumed private compilation.");
            var plan = plans.SingleOrDefault(item => ReferenceEquals(item.Slice, announcement))
                ?? throw new UnauthorizedAccessException("The announcement slice is not part of this original.");
            await issuer.RequireAnnouncementAsync(announcement, cancellationToken);
            await RequireCurrentAsync(cancellationToken);
            foreach (var effect in plan.Effects)
                await issuer.RequireOwnedAsync(issuer.CurrentProof
                    ?? throw new UnauthorizedAccessException("The original import proof is not active."), effect, cancellationToken);
            if (plan.Effects.IsEmpty)
                return [];
            var after = plan.After ?? throw new InvalidOperationException("A publication change requires its canonical row.");
            writing = true;
            try
            {
                if (context.Database.IsNpgsql())
                    await PostgresConfigurationImportAnnouncementContract.WriteAsync(
                        context, enrollment!, announcement.Intent.Owner.TenantId, plan.Before, after, cancellationToken);
                else
                    // This stateless row primitive keeps the captured import transaction and
                    // avoids flushing unrelated tracked legal/document effects through SaveChanges.
                    await TypedSettingsSqliteWriter.WriteAsync(context, transaction,
                        announcement.Intent.Owner.TenantId.HasValue ? typeof(TenantSetting) : typeof(SystemSetting),
                        Guid.Parse(after[nameof(SystemSetting.Id)]!.GetValue<string>()), plan.Before, after, cancellationToken);
                expected[announcement.Intent.Owner] = after.DeepClone().AsObject();
            }
            finally { writing = false; }
            await RequireCurrentAsync(cancellationToken);
            await issuer.RequireAnnouncementAsync(announcement, cancellationToken);
            bool locked = after[nameof(SystemSetting.IsLocked)]!.GetValue<bool>();
            return [new(Key, plan.Before?[nameof(SystemSetting.Value)]?.GetValue<string>(),
                after[nameof(SystemSetting.Value)]!.GetValue<string>(),
                announcement.Intent.Owner.TenantId.HasValue ? SettingSource.TenantOverride
                    : locked ? SettingSource.SystemLocked : SettingSource.SystemDefault,
                announcement.Intent.Owner.TenantId, authority.Subject.PrincipalId, occurredAt)];
        }
        catch { failed = true; throw; }
    }

    public async Task RequireCompletedAsync(CancellationToken cancellationToken)
    {
        try
        {
            RequireSelection();
            if (plans.IsDefault || completed.Count != plans.Length || plans.Any(plan => !completed.Contains(plan.Slice)))
                throw new UnauthorizedAccessException("Every original announcement import intent must privately complete.");
            await RequireCurrentAsync(cancellationToken);
        }
        catch { failed = true; throw; }
    }

    private void RequireSlice(HumanConfigurationImportAnnouncement slice)
    {
        RequireSelection();
        if (!ReferenceEquals(slice.Store, this) || !ReferenceEquals(slice.Subject, authority.Subject)
            || slice.OperationId != operationId || slice.ArtifactDigest != digest || slice.Target != authority.Target)
            throw new UnauthorizedAccessException("The announcement slice belongs to another import owner.");
    }

    private void RequireSelection()
    {
        lease.RequireOriginalOwner(context);
        if (failed || !ReferenceEquals(lease.HumanScope?.Subject, authority.Subject)
            || !ReferenceEquals(lease.HumanLifetime, authority.Lifetime)
            || lease.HumanScope.Purpose != (authority.Target.Scope == ConfigurationImportScope.Instance
                ? HumanAdministratorCommitPurpose.ConfigurationImport : HumanAdministratorCommitPurpose.RuntimeTenant)
            || lease.SelectedTenantId != authority.Target.TenantId
            || lease.PolicyPurpose is not (null or PolicyMutationPurpose.Standard)
            || context.IsConfigurationInstanceImportOwner != (authority.Target.Scope == ConfigurationImportScope.Instance)
            || context.IsTenantFilterBypassed || context.ContextId != contextId || context.HumanCommitContextLease != contextId
            || !ReferenceEquals(context.HumanCommitAuditSubject, authority.Subject)
            || !connection.Matches(context) || connection.State != ConnectionState.Open
            || !ReferenceEquals(transaction, context.Database.CurrentTransaction?.GetDbTransaction())
            || transaction.IsolationLevel != IsolationLevel.Serializable || !connection.Owns(transaction)
            || System.Transactions.Transaction.Current is not null
            || tables != Table(typeof(SystemSetting)) + "\n" + Table(typeof(TenantSetting)))
            throw new UnauthorizedAccessException("Announcement import lost its original subject, connector or transaction.");
    }

    private async Task<Dictionary<AnnouncementOwner, JsonObject?>> ReadImageAsync(CancellationToken token)
    {
        RequireSelection();
        var image = new Dictionary<AnnouncementOwner, JsonObject?>();
        var tenants = intents.Where(intent => intent.Owner.TenantId.HasValue)
            .Select(intent => intent.Owner.TenantId!.Value).Order().ToArray();
        if (context.Database.IsNpgsql())
        {
            var current = await PostgresConfigurationImportAnnouncementContract.ReadAsync(
                context, enrollment!, authority.Target.TenantId, tenants, token);
            image.Add(AnnouncementOwner.Instance, current["System"]?.DeepClone().AsObject());
            var rows = current["Tenants"]!.AsArray().OfType<JsonObject>()
                .ToDictionary(row => Guid.Parse(row[nameof(TenantSetting.TenantId)]!.GetValue<string>()));
            foreach (Guid tenant in tenants)
            {
                image.Add(AnnouncementOwner.ForTenant(tenant), rows.GetValueOrDefault(tenant)?.DeepClone().AsObject());
            }
            return image;
        }
        var system = await context.SystemSettings.AsNoTracking().SingleOrDefaultAsync(row => row.SettingKey == Key, token);
        image.Add(AnnouncementOwner.Instance, system is null ? null : Snapshot(system, typeof(SystemSetting)));
        var rowsForTenants = await context.Set<TenantSetting>().AsNoTracking()
            .Where(row => tenants.Contains(row.TenantId) && row.SettingKey == Key).ToListAsync(token);
        foreach (Guid tenant in tenants)
        {
            var row = rowsForTenants.SingleOrDefault(value => value.TenantId == tenant);
            image.Add(AnnouncementOwner.ForTenant(tenant), row is null ? null : Snapshot(row, typeof(TenantSetting)));
        }
        return image;
    }

    private static AnnouncementSourceSnapshot Source(AnnouncementOwner owner, JsonObject? row) =>
        new(owner, AnnouncementSettingValueCodec.DeserializeGroup(row?[nameof(SystemSetting.Value)]?.GetValue<string>()),
            row?[nameof(SystemSetting.IsLocked)]?.GetValue<bool>() ?? false);

    private JsonObject BuildAfter(AnnouncementOwner owner, JsonObject? before, string value, bool locked)
    {
        if (before is not null)
        {
            var after = before.DeepClone().AsObject();
            after[nameof(SystemSetting.Value)] = value;
            after[nameof(SystemSetting.IsLocked)] = locked;
            after[nameof(SystemSetting.UpdatedAt)] = occurredAt.ToString("O", CultureInfo.InvariantCulture);
            after[nameof(SystemSetting.UpdatedBy)] = authority.Subject.PrincipalId.ToString("D");
            return after;
        }
        if (owner.TenantId is { } tenant)
            return Snapshot(new TenantSetting
            {
                Id = Guid.CreateVersion7(), TenantId = tenant, Tenant = null!, SettingKey = Key,
                Value = value, IsLocked = locked, CreatedAt = occurredAt, CreatedBy = authority.Subject.PrincipalId
            }, typeof(TenantSetting));
        var definition = SettingRegistry.Get(Key)
            ?? throw new InvalidOperationException("The announcement group definition is missing.");
        return Snapshot(new SystemSetting
        {
            Id = Guid.CreateVersion7(), SettingKey = Key, Value = value, IsLocked = locked,
            ValueType = definition.ValueType, Description = definition.Description, Category = definition.Category,
            CreatedAt = occurredAt, CreatedBy = authority.Subject.PrincipalId
        }, typeof(SystemSetting));
    }

    private JsonObject Snapshot(object entity, Type type)
    {
        var row = new JsonObject();
        foreach (var property in context.Model.FindEntityType(type)!.GetProperties())
        {
            object? value = property.GetGetter().GetClrValue(entity);
            if (property.GetRelationalTypeMapping().StoreType is "json" or "jsonb")
                row[property.Name] = value is string json ? JsonNode.Parse(json) : null;
            else
            {
                if (value is Guid guid) value = guid.ToString("D");
                else if (value is DateTime date)
                    value = DateTime.SpecifyKind(date, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
                row[property.Name] = JsonSerializer.SerializeToNode(value, value?.GetType() ?? typeof(object));
            }
        }
        return row;
    }

    private string Table(Type type) => new HumanPrincipalSourceSql(context).Table(type);
    private sealed record Plan(HumanConfigurationImportAnnouncement Slice, JsonObject? Before,
        JsonObject? After, ImmutableArray<HumanConfigurationImportSettingEffect> Effects);
}
