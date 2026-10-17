using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.EmailDispatch;
using Explore.Application.Features.Settings;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Explore.Persistence.Services;

/// <summary>
/// One private observation and completion ledger on the source-selected mixed context.
/// Real writers enlist canonical rows here; no public scalar or callback grant is exposed.
/// </summary>
public sealed partial class MixedSettingsBatchStore(
    ExploreDbContext context, IServiceProvider services, TimeProvider clock) : IMixedSettingsBatchStore
{
    private static readonly ConditionalWeakTable<ExploreDbContext, MixedSettingsBatchStore> Active = new();
    private UpdateSettingBatchCommand? original;
    private IHumanMixedSettingsSource? source;
    private ISerializableAttemptOwner? owner;
    private MixedSettingsBatchMutation? mutation;
    private DbContextId contextId;
    private RelationalConnectionIdentity? connection;
    private DbTransaction? transaction;
    private string? before;
    private JsonObject? expected;
    private PostgresNativeQueueEnrollment? enrollment;
    private bool compiled;
    private bool completed;
    private IConfiguration? deploymentSource;
    private ImmutableDictionary<string, string?>? deploymentValues;
    internal IConfiguration Deployment { get; private set; } = null!;
    private readonly Dictionary<string, Completion> completions = new(StringComparer.Ordinal);
    private readonly List<RowEffect> emitted = [];

    public DateTime OccurredAt { get; private set; }
    internal MixedSettingsBatchMutation Mutation => mutation ?? throw new UnauthorizedAccessException("Mixed acquisition is required.");

    public Task ObserveAsync(UpdateSettingBatchCommand request, IHumanMixedSettingsSource originalSource,
        ISerializableAttemptOwner originalOwner, CancellationToken cancellationToken)
    {
        services.GetRequiredService<IHumanEmailMutationPreparation>().RequireMixedSource(originalSource);
        var selection = services.GetRequiredService<ExploreDbContextExecutionLease>();
        selection.RequireOriginalOwner(context);
        if (original is not null || !ReferenceEquals(context, services.GetRequiredService<ExploreDbContext>())
            || !ReferenceEquals(originalOwner, services.GetRequiredService<ISerializableAttemptOwner>())
            || selection.PolicyPurpose != PolicyMutationPurpose.MixedSettings
            || !ReferenceEquals(selection.HumanScope?.Subject, originalSource.Subject)
            || selection.SelectedTenantId != originalSource.TenantId || context.Database.CurrentTransaction is not null)
            throw new UnauthorizedAccessException("Mixed storage observation requires the original idle selected unit.");
        original = request;
        source = originalSource;
        owner = originalOwner;
        contextId = context.ContextId;
        connection = RelationalConnectionIdentity.Capture(context);
        enrollment = selection.HumanScope!.Enrollment;
        before = originalSource.OriginalData;
        expected = JsonNode.Parse(before)!.AsObject();
        deploymentSource = services.GetRequiredService<IConfiguration>();
        deploymentValues = CaptureDeployment(deploymentSource);
        Deployment = new ConfigurationBuilder().AddInMemoryCollection(deploymentValues).Build();
        OccurredAt = clock.GetUtcNow().UtcDateTime;
        if ((RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql))
            OccurredAt = new DateTime(OccurredAt.Ticks - OccurredAt.Ticks % 10, DateTimeKind.Utc);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task AcquireAsync(MixedSettingsBatchMutation prepared, CancellationToken cancellationToken)
    {
        RequireSelection();
        if (mutation is not null || !ReferenceEquals(prepared.Original, original)
            || !ReferenceEquals(prepared.Source, source) || Active.TryGetValue(context, out _)
            || !ReferenceEquals(owner, services.GetRequiredService<ISerializableAttemptOwner>()))
            throw new UnauthorizedAccessException("Mixed storage cannot change or reacquire its original preparation.");
        transaction = RequireTransaction();
        mutation = prepared;
        Active.Add(context, this);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ResolvedSetting>> ResolveOriginalAsync(MixedSettingsBatchMutation prepared,
        CancellationToken cancellationToken)
    {
        Require(prepared, requireCompilation: false);
        cancellationToken.ThrowIfCancellationRequested();
        var system = MixedSettingsDataImage.Rows<SystemSetting>(before!).ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
        var tenants = source!.TenantId is { } tenant
            ? MixedSettingsDataImage.Rows<TenantSetting>(before!).Where(row => row.TenantId == tenant)
                .ToDictionary(row => row.SettingKey, StringComparer.Ordinal) : null;
        IReadOnlyList<ResolvedSetting> resolved = original!.Values.Keys.Select(key =>
            HierarchicalSettingMerge.Resolve(key, system, tenants) ?? new ResolvedSetting { Key = key }).ToArray();
        return Task.FromResult(resolved);
    }

    public async Task CompileAsync(MixedSettingsBatchMutation prepared, CancellationToken cancellationToken)
    {
        Require(prepared, requireCompilation: false);
        if (compiled) throw new UnauthorizedAccessException("The mixed group can only be compiled once.");
        var category = SettingRegistry.GetByCategory(original!.Category).Select(definition => definition.Key).ToHashSet(StringComparer.Ordinal);
        if (prepared.Accepted.Any(pair => !original.Values.ContainsKey(pair.Key) || !category.Contains(pair.Key)
                || SettingRegistry.Get(pair.Key) is not { } definition
                || original.Scope < definition.MinScope || original.Scope > definition.MaxScope))
            throw new UnauthorizedAccessException("Accepted mixed effects must belong to the original category and scope.");
        await source!.RequireSourcesAsync(cancellationToken);
        if (!MixedSettingsDataImage.Same(before!, await ReadDataAsync(cancellationToken)))
            throw new UnauthorizedAccessException("Original mixed data changed before compilation.");
        if ((RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql))
            OccurredAt = await PostgresMixedSettingsContract.CompileAsync(context, enrollment!, source.TenantId,
                original, prepared.Accepted, cancellationToken);
        compiled = true;
    }

    public async Task ApplyOrdinaryAsync(MixedSettingsBatchMutation prepared, string key, CancellationToken cancellationToken)
    {
        Require(prepared);
        if (!prepared.Accepted.TryGetValue(key, out string? value) || EmailDeliverySettingKeys.Contains(key)
            || EventResourceSettingMutationGuard.Handles(key) || VisitorAccessSettingMutationGuard.Handles(key)
            || PublicationPolicySettingKeys.All.Contains(key, StringComparer.Ordinal)
            || LocationPrivacySettingDefinitions.All.Any(definition => definition.Key == key))
            throw new UnauthorizedAccessException("The original ordinary mixed slice is required.");
        Explore.Domain.Ai.AiCredentialReference.RejectRetiredSettingMutation(key);
        EventResourceProviderBindingDocument.RejectGenericMutation(key);
        InstanceOperatorIdentitySettingKeys.RejectGenericMutation(key);
        await ApplyScalarAsync(prepared, key, value, true, cancellationToken);
    }

    private async Task ApplyScalarAsync(MixedSettingsBatchMutation prepared, string key, string value,
        bool complete, CancellationToken cancellationToken)
    {
        object row;
        EntityState state;
        if (source!.TenantId is { } tenant)
        {
            var system = Rows<SystemSetting>().SingleOrDefault(item => item.SettingKey == key);
            if (system?.IsLocked == true)
                throw new UnauthorizedAccessException("The original tenant setting is instance locked.");
            var existing = Rows<TenantSetting>().SingleOrDefault(item => item.TenantId == tenant && item.SettingKey == key);
            state = existing is null ? EntityState.Added : EntityState.Modified;
            var setting = existing ?? new TenantSetting
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant,
                Tenant = null!,
                SettingKey = key,
                Value = value,
                IsLocked = false,
                CreatedAt = OccurredAt,
                CreatedBy = prepared.Actor
            };
            if (existing is not null)
            {
                setting.Value = value;
                setting.UpdatedAt = OccurredAt;
                setting.UpdatedBy = prepared.Actor;
            }
            row = setting;
        }
        else
        {
            var existing = Rows<SystemSetting>().SingleOrDefault(item => item.SettingKey == key);
            var definition = SettingRegistry.Get(key)!;
            state = existing is null ? EntityState.Added : EntityState.Modified;
            var setting = existing ?? new SystemSetting
            {
                Id = Guid.CreateVersion7(),
                SettingKey = key,
                Value = value,
                ValueType = definition.ValueType,
                IsLocked = false,
                Description = definition.Description,
                Category = definition.Category,
                DisplayOrder = 0,
                CreatedAt = OccurredAt,
                CreatedBy = prepared.Actor
            };
            if (existing is not null)
            {
                setting.Value = value;
                setting.UpdatedAt = OccurredAt;
                setting.UpdatedBy = prepared.Actor;
            }
            row = setting;
        }
        await EmitAsync("settings", [key], [(row, state)], complete, cancellationToken);
        if (complete) completions.Add(key, new(this, prepared, key, value));
    }

    public async Task RequireCompletedAsync(MixedSettingsBatchMutation prepared, CancellationToken cancellationToken)
    {
        Require(prepared);
        if (completed || prepared.Accepted.Any(pair => !completions.TryGetValue(pair.Key, out var receipt)
                || !ReferenceEquals(receipt.Mutation, prepared) || receipt.Key != pair.Key || receipt.Value != pair.Value))
            throw new UnauthorizedAccessException("Every accepted mixed slice requires its real private writer completion.");
        foreach (var pair in prepared.Accepted)
        {
            string? value = prepared.TenantId is { } tenant
                ? Rows<TenantSetting>().SingleOrDefault(row => row.TenantId == tenant && row.SettingKey == pair.Key)?.Value
                : Rows<SystemSetting>().SingleOrDefault(row => row.SettingKey == pair.Key)?.Value;
            if (value != pair.Value)
                throw new UnauthorizedAccessException("An accepted mixed effect was not persisted by its writer.");
        }
        string actual = await ReadDataAsync(cancellationToken);
        if (!MixedSettingsDataImage.Same(expected!.ToJsonString(), actual))
            throw new UnauthorizedAccessException("Mixed rows, absence, metadata or projections changed after their writers completed.");
        if ((RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql))
            await PostgresMixedSettingsContract.CompleteAsync(context, enrollment!, source!.TenantId,
                expected.ToJsonString(), cancellationToken);
        else
            foreach (var effect in emitted)
            {
                var stored = await MixedSettingsDataImage.ReadSqliteEffectAsync(context, effect.Type, effect.Id, cancellationToken);
                if (!JsonNode.DeepEquals(effect.After, stored))
                    throw new UnauthorizedAccessException("A completed mixed writer effect changed.");
            }
        Require(prepared);
        completed = true;
    }

    public void Close(MixedSettingsBatchMutation prepared)
    {
        if (mutation is null) return;
        if (!ReferenceEquals(mutation, prepared))
            throw new UnauthorizedAccessException("Only the original mixed owner can close storage custody.");
        Active.Remove(context);
        transaction = null;
    }

    internal static MixedSettingsBatchStore? Current(ExploreDbContext context)
    {
        if (!Active.TryGetValue(context, out var store)) return null;
        store.Require(store.Mutation);
        return store;
    }

    internal static MixedSettingsBatchStore RequireOriginal(ExploreDbContext context,
        IServiceProvider services, MixedSettingsBatchMutation prepared)
    {
        var store = Current(context) ?? throw new UnauthorizedAccessException("The original mixed writer owner is required.");
        if (!ReferenceEquals(store, services.GetRequiredService<IMixedSettingsBatchStore>()))
            throw new UnauthorizedAccessException("The mixed writer belongs to another scoped issuer.");
        store.Require(prepared);
        return store;
    }

    internal ImmutableArray<T> Rows<T>() => MixedSettingsDataImage.Rows<T>(expected!.ToJsonString());

    public string ExpectedSourceData(IHumanMixedSettingsSource originalSource)
    {
        RequireSelection();
        if (!ReferenceEquals(source, originalSource) || expected is null || mutation is not null && transaction is null)
            throw new UnauthorizedAccessException("The original mixed source cannot borrow another writer image.");
        return expected.ToJsonString();
    }

    public Task<ResolvedSetting?> ResolveOriginalInstanceAsync(MixedSettingsBatchMutation prepared,
        string key, CancellationToken cancellationToken)
    {
        Require(prepared, requireCompilation: false);
        if (!MixedSettingsDataImage.Keys(original!).Contains(key, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("This key is outside the original mixed read closure.");
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(HierarchicalSettingMerge.Resolve(key, MixedSettingsDataImage.Rows<SystemSetting>(before!)
            .ToDictionary(row => row.SettingKey, StringComparer.Ordinal)));
    }

    private void RequireSelection()
    {
        var selected = services.GetRequiredService<ExploreDbContextExecutionLease>();
        selected.RequireOriginalOwner(context);
        services.GetRequiredService<IHumanEmailMutationPreparation>().RequireMixedSource(
            source ?? throw new UnauthorizedAccessException("An original mixed storage observation is required."));
        if (selected.PolicyPurpose != PolicyMutationPurpose.MixedSettings || context.ContextId != contextId
            || connection is null || !connection.Matches(context)
            || selected.SelectedTenantId != source.TenantId || !ReferenceEquals(selected.HumanScope?.Subject, source.Subject))
            throw new UnauthorizedAccessException("Original mixed storage custody changed.");
        if (deploymentValues is not null && (!ReferenceEquals(deploymentSource, services.GetRequiredService<IConfiguration>())
                || !deploymentValues.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .SequenceEqual(CaptureDeployment(deploymentSource!).OrderBy(pair => pair.Key, StringComparer.Ordinal))))
            throw new UnauthorizedAccessException("Original mixed deployment projection inputs changed.");
    }

    private DbTransaction RequireTransaction()
    {
        RequireSelection();
        if (context.Database.CurrentTransaction?.GetDbTransaction() is not { } physical
            || physical.IsolationLevel != IsolationLevel.Serializable || !connection!.Owns(physical)
            || System.Transactions.Transaction.Current is not null)
            throw new UnauthorizedAccessException("Mixed storage requires its original Serializable unit.");
        return physical;
    }

    private void Require(MixedSettingsBatchMutation prepared, bool requireCompilation = true, bool allowCompleted = false)
    {
        prepared.RequireActive();
        if (!ReferenceEquals(mutation, prepared) || !ReferenceEquals(transaction, RequireTransaction())
            || completed && !allowCompleted || requireCompilation && !compiled)
            throw new UnauthorizedAccessException("The original active mixed compilation is required.");
    }

    private Task<string> ReadDataAsync(CancellationToken token) => (RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql)
        ? PostgresMixedSettingsContract.ReadDataAsync(context, enrollment!, original!, source!.TenantId, token)
        : MixedSettingsDataImage.ReadSqliteAsync(context, original!, source!.TenantId, token);

    private JsonObject Snapshot(object value, Type type)
    {
        var result = new JsonObject();
        foreach (var property in context.Model.FindEntityType(type)!.GetProperties().OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            object? item = property.GetGetter().GetClrValue(value);
            if (item is Guid guid) item = guid.ToString("D");
            else if (item is DateTime date) item = DateTime.SpecifyKind(date, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
            else if (item is DateTimeOffset offset) item = offset.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
            if (item is string json && property.GetRelationalTypeMapping().StoreType is "json" or "jsonb")
                result[property.Name] = JsonNode.Parse(json);
            else
                result[property.Name] = JsonSerializer.SerializeToNode(item, item?.GetType() ?? typeof(object));
        }
        return result;
    }

    private static ImmutableDictionary<string, string?> CaptureDeployment(IConfiguration configuration)
    {
        var values = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
        foreach (string key in new[] { "Authentication:Provider", "AUTHENTICATION_PROVIDER", "Keycloak:Authority", "Keycloak:ClientId" })
            values.Add(key, configuration[key]);
        foreach (var child in configuration.GetSection("Secrets:Ownership:DeploymentManagedKeys").GetChildren())
            values.Add(child.Path, child.Value);
        return values.ToImmutable();
    }

    private async Task EmitAsync(string slice, ImmutableArray<string> keys,
        ImmutableArray<(object Entity, EntityState State)> writes, bool complete, CancellationToken token)
    {
        Require(Mutation);
        if (keys.IsDefault || keys.Distinct(StringComparer.Ordinal).Count() != keys.Length
            || keys.Any(key => !Mutation.Accepted.ContainsKey(key) || completions.ContainsKey(key)))
            throw new UnauthorizedAccessException("A mixed writer cannot widen or replay its compiled slice.");
        await source!.RequireSourcesAsync(token);
        if (!MixedSettingsDataImage.Same(expected!.ToJsonString(), await ReadDataAsync(token)))
            throw new UnauthorizedAccessException("The mixed writer no longer has its original source image.");
        var rows = new JsonArray();
        var effects = new List<RowEffect>();
        foreach (var (entity, state) in writes)
        {
            Type type = entity.GetType();
            if (!MixedSettingsDataImage.WriteTypes.Contains(type) || state is not (EntityState.Added or EntityState.Modified))
                throw new UnauthorizedAccessException("The mixed writer emitted a foreign effect.");
            var after = Snapshot(entity, type);
            Guid id = Guid.Parse(after["Id"]!.GetValue<string>());
            if (entity is SystemSetting system && (Mutation.TenantId.HasValue
                    || !keys.Contains(system.SettingKey) || !Mutation.Accepted.TryGetValue(system.SettingKey, out string? systemValue)
                    || system.Value != systemValue || (slice == "email") != EmailDeliverySettingKeys.Contains(system.SettingKey))
                || entity is TenantSetting tenant && (tenant.TenantId != Mutation.TenantId
                    || !keys.Contains(tenant.SettingKey) || !Mutation.Accepted.TryGetValue(tenant.SettingKey, out string? tenantValue)
                    || tenant.Value != tenantValue || (slice == "email") != EmailDeliverySettingKeys.Contains(tenant.SettingKey)))
                throw new UnauthorizedAccessException("A mixed writer cannot widen its original setting effect.");
            var old = expected[type.Name]?.AsArray().OfType<JsonObject>()
                .SingleOrDefault(row => row["Id"]!.GetValue<string>() == id.ToString("D"));
            if ((state == EntityState.Added) != (old is null)
                && MixedSettingsDataImage.ReadTypes.Contains(type))
                throw new UnauthorizedAccessException("The mixed writer changed original row presence.");
            rows.Add(new JsonObject { ["Type"] = type.Name, ["Before"] = old?.DeepClone(), ["After"] = after.DeepClone() });
            effects.Add(new(type, id, after));
        }
        if ((RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql))
            await PostgresMixedSettingsContract.WriteAsync(context, enrollment!, source.TenantId, slice,
                new JsonObject
                {
                    ["Keys"] = JsonSerializer.SerializeToNode(keys),
                    ["Rows"] = rows,
                    ["Complete"] = complete
                }.ToJsonString(), token);
        else if (!writes.IsEmpty)
        {
            if (context.ChangeTracker.Entries().Any(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && !writes.Any(write => ReferenceEquals(write.Entity, entry.Entity))))
                throw new UnauthorizedAccessException("An unrelated tracked write cannot join a mixed slice.");
            foreach (var (entity, state) in writes)
                context.Entry(entity).State = state;
            await context.SaveChangesAsync(token);
        }
        foreach (var effect in effects)
        {
            if (expected[effect.Type.Name] is JsonArray collection)
            {
                var retained = collection.OfType<JsonObject>().Where(row => row["Id"]!.GetValue<string>() != effect.Id.ToString("D"))
                    .Select(row => row.DeepClone()).Append(effect.After.DeepClone())
                    .OrderBy(row => row!["Id"]!.ToString(), StringComparer.Ordinal).ToArray();
                expected[effect.Type.Name] = new JsonArray(retained);
            }
            emitted.RemoveAll(previous => previous.Type == effect.Type && previous.Id == effect.Id);
            emitted.Add(effect);
        }
        foreach (var (entity, _) in writes)
            context.Entry(entity).State = EntityState.Detached;
        if ((RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql))
        {
            // The independent mixed source reservation already fences tenant creation.
            // Retain the existing disclosure algorithm's exact enrollment side effects;
            // the original unit flushes them before terminal source/time admission.
            if (writes.Any(write => write.Entity is SystemSetting setting
                    && EventDiscoveryDisclosureMutationScope.IsDisclosureSetting(setting.SettingKey)))
                context.DisclosureMutations.Enlist(MixedSettingsDataImage.TenantIds(expected!.ToJsonString()));
            context.DisclosureMutations.Enlist(writes.Select(write => write.Entity).SelectMany(entity => entity switch
            {
                TenantSetting setting when EventDiscoveryDisclosureMutationScope.IsDisclosureSetting(setting.SettingKey) => new[] { setting.TenantId },
                EventLocation location => new[] { location.TenantId },
                _ => Array.Empty<Guid>()
            }));
        }
        Require(Mutation);
    }

    private sealed record Completion(object Writer, MixedSettingsBatchMutation Mutation, string Key, string Value);
    private sealed record RowEffect(Type Type, Guid Id, JsonObject After);
}
