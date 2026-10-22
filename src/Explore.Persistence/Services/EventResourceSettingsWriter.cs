using System.Collections.Immutable;
using Explore.Persistence.Database.ProviderPrimitives;
using System.Data;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Notifications;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Domain.ValueObjects;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Services;

public sealed class EventResourceSettingsWriter : IEventResourceSettingsWriter
{
    private readonly ExploreDbContext context;
    private readonly ISettingMutationLock mutationLock;
    private readonly IUnitOfWork unitOfWork;
    private readonly IInstanceAdministratorCommitAuthority? instanceAuthority;
    private readonly ITenantAdministratorCommitAuthority? tenantAuthority;
    private readonly IHumanActionCommitFinalizer? finalizer;
    private readonly HumanCommitSubject? originalSubject;
    private readonly IHumanSettingsMutationPreparation? settingsPreparation;
    private readonly IHumanConfigurationImportAdmission? importAdmission;
    private readonly TimeProvider clock;

    public EventResourceSettingsWriter(
        ExploreDbContext context, ISettingMutationLock mutationLock, IUnitOfWork unitOfWork)
    {
        this.context = context;
        this.mutationLock = mutationLock;
        this.unitOfWork = unitOfWork;
        clock = TimeProvider.System;
    }

    public EventResourceSettingsWriter(
        ExploreDbContext context, ISettingMutationLock mutationLock, IUnitOfWork unitOfWork,
        IInstanceAdministratorCommitAuthority instanceAuthority,
        ITenantAdministratorCommitAuthority tenantAuthority, IHumanActionCommitFinalizer finalizer,
        HumanCommitSubject originalSubject, IHumanSettingsMutationPreparation settingsPreparation,
        TimeProvider clock,
        IHumanConfigurationImportAdmission? importAdmission = null)
        : this(context, mutationLock, unitOfWork)
    {
        this.instanceAuthority = instanceAuthority;
        this.tenantAuthority = tenantAuthority;
        this.finalizer = finalizer;
        this.originalSubject = originalSubject;
        this.settingsPreparation = settingsPreparation;
        this.importAdmission = importAdmission;
        this.clock = clock;
    }

    public async Task<EventResourceSettingsWriteResult> ApplyAsync(
        ImmutableArray<EventResourceSettingMutation> mutations,
        HumanCommitSubject subject,
        SettingsMutationOperation operation,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (finalizer is null || !ReferenceEquals(subject, originalSubject)
            || !context.HasHumanCommitContextLease
            || !ReferenceEquals(context.HumanCommitAuditSubject, subject)
            || actorUserId is { } actor && actor != subject.PrincipalId)
            throw new UnauthorizedAccessException("Human resource writes require a prepared committing owner.");
        if (!Enum.IsDefined(operation))
            throw new ArgumentOutOfRangeException(nameof(operation));
        if ((operation == SettingsMutationOperation.ConfigurationInstanceImport)
            != context.IsConfigurationInstanceImportOwner)
            throw new InvalidOperationException("Resource mutation action must match the selected import owner.");
        if (operation is SettingsMutationOperation.ConfigurationInstanceImport
                or SettingsMutationOperation.ConfigurationTenantImport
            && context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Resource import must borrow its existing serializable owner.");
        if (operation is SettingsMutationOperation.ConfigurationInstanceImport
            or SettingsMutationOperation.ConfigurationTenantImport)
        {
            var producer = importAdmission
                ?? throw new UnauthorizedAccessException("The original acquired import producer is required.");
            var slice = mutations.Select(mutation => new HumanConfigurationImportSettingEffect(
                mutation.TenantId.HasValue ? SettingScope.Tenant : SettingScope.Instance,
                mutation.TenantId, mutation.Key, mutation.Kind switch
                {
                    EventResourceSettingMutationKind.SetValue => HumanSettingsMutationKind.SetValue,
                    EventResourceSettingMutationKind.SetLock => HumanSettingsMutationKind.SetLock,
                    EventResourceSettingMutationKind.Remove => HumanSettingsMutationKind.Remove,
                    _ => throw new ArgumentOutOfRangeException(nameof(mutations))
                }, mutation.Value, mutation.IsLocked)).ToImmutableArray();
            return await producer.ExecuteJoinedAsync(operation, slice,
                (_, token) => ApplyCoreAsync(mutations, subject, operation, actorUserId, token), cancellationToken);
        }
        var preparation = settingsPreparation
            ?? throw new UnauthorizedAccessException("Original resource source preparation is required.");
        var effects = mutations.Select(mutation => new HumanSettingsMutation(
            operation switch
            {
                SettingsMutationOperation.InstanceSettings => HumanSettingsOperation.InstanceSettings,
                SettingsMutationOperation.TenantSettings => HumanSettingsOperation.TenantSettings,
                SettingsMutationOperation.InstanceTenantSettings => HumanSettingsOperation.InstanceTenantSettings,
                _ => throw new InvalidOperationException("The original resource Settings action is required.")
            }, mutation.TenantId.HasValue ? SettingScope.Tenant : SettingScope.Instance,
            mutation.TenantId, null, mutation.Key, mutation.Kind switch
            {
                EventResourceSettingMutationKind.SetValue => HumanSettingsMutationKind.SetValue,
                EventResourceSettingMutationKind.SetLock => HumanSettingsMutationKind.SetLock,
                EventResourceSettingMutationKind.Remove => HumanSettingsMutationKind.Remove,
                _ => throw new ArgumentOutOfRangeException(nameof(mutations))
            }, mutation.Value, mutation.IsLocked)).ToImmutableArray();
        if (context.Database.CurrentTransaction is not null)
            return await preparation.ExecuteCurrentJoinedAsync(effects,
                (_, token) => ApplyCoreAsync(mutations, subject, operation, actorUserId, token), cancellationToken);
        var prepared = await preparation.PrepareResourceAsync(mutations, operation, actorUserId, cancellationToken);
        Exception? primaryFailure = null;
        try
        {
            return await mutationLock.ExecuteOrderedGroupsAsync([EventResourceSettingMutationGuard.Keys],
                token => preparation.ExecutePreparedAsync(prepared,
                    (_, inner) => ApplyCoreAsync(mutations, subject, operation, actorUserId, inner), token),
                cancellationToken);
        }
        catch (Exception failure) { primaryFailure = failure; throw; }
        finally { await preparation.CloseAsync(prepared, primaryFailure); }
    }

    public Task<EventResourceSettingsWriteResult> ApplyCurrentHumanAsync(
        ImmutableArray<EventResourceSettingMutation> mutations, SettingsMutationOperation operation,
        Guid? actorUserId, CancellationToken cancellationToken = default) =>
        ApplyAsync(mutations, originalSubject
            ?? throw new UnauthorizedAccessException("Direct human resource writes require a selected authenticated owner."),
            operation, actorUserId, cancellationToken);

    public Task<EventResourceSettingsWriteResult> ApplyInternalAsync(
        ImmutableArray<EventResourceSettingMutation> mutations,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (originalSubject is not null || context.HumanCommitContextLease.HasValue
            || context.HumanCommitAuditSubject is not null)
            throw new UnauthorizedAccessException("A human resource writer cannot enter internal setup.");
        if (context.IsConfigurationInstanceImportOwner)
            throw new InvalidOperationException("Instance import requires its trusted human action.");
        return ApplyCoreAsync(mutations, null, null, actorUserId, cancellationToken);
    }

    private Task<EventResourceSettingsWriteResult> ApplyCoreAsync(
        ImmutableArray<EventResourceSettingMutation> mutations,
        HumanCommitSubject? subject,
        SettingsMutationOperation? operation,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        if (mutations.IsDefault || mutations.Any(mutation => !IsValid(mutation))
            || mutations.Select(mutation => (mutation.TenantId, mutation.Key)).Distinct().Count() != mutations.Length)
            return Task.FromResult(Rejected("event_resource_policy_invalid"));
        if (mutations.IsEmpty && subject is null)
            return Task.FromResult(new EventResourceSettingsWriteResult(true, null, []));
        if ((RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql) && context.IsConfigurationInstanceImportOwner)
        {
            PostgresConfigurationImportEventResourceContract.ValidateMutations(mutations, actorUserId);
            if (context.Database.CurrentTransaction is null)
                throw new InvalidOperationException("Resource import must join its existing serializable transaction.");
        }
        if (context.Database.CurrentTransaction is null && subject is not null)
            return mutationLock.ExecuteOrderedGroupsAsync([EventResourceSettingMutationGuard.Keys],
                token => ((ISerializableAttemptOwner)unitOfWork).ExecuteAsync(
                    inner => ApplyInsideTransactionAsync(mutations, subject, operation, actorUserId, inner), token),
                cancellationToken);
        if (context.Database.CurrentTransaction is null)
            return mutationLock.ExecuteOrderedGroupsAsync([EventResourceSettingMutationGuard.Keys],
                token => unitOfWork.ExecuteSerializableAsync(
                    inner => ApplyInsideTransactionAsync(mutations, subject, operation, actorUserId, inner), token), cancellationToken);
        if (context.Database.CurrentTransaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Resource policy writes require a serializable transaction.");
        return mutationLock.ExecuteManyAsync(EventResourceSettingMutationGuard.Keys,
            token => ApplyInsideTransactionAsync(mutations, subject, operation, actorUserId, token), cancellationToken);
    }

    private async Task<EventResourceSettingsWriteResult> ApplyInsideTransactionAsync(
        ImmutableArray<EventResourceSettingMutation> mutations, HumanCommitSubject? subject,
        SettingsMutationOperation? operation, Guid? actorUserId, CancellationToken token)
    {
        var mixed = MixedSettingsBatchStore.Current(context);
        async Task RequireCurrentAuthorityAsync(CancellationToken cancellationToken)
        {
            if (mixed is not null)
            {
                await mixed.RequireSpecializedAsync(subject, operation, actorUserId,
                    mutations.Select(mutation => mutation.Key), cancellationToken);
                return;
            }
            if (subject is null)
                return;
            context.ValidateTenantLease();
            bool tenantAction = operation is SettingsMutationOperation.TenantSettings
                or SettingsMutationOperation.ConfigurationTenantImport;
            bool targetsMatch = tenantAction || operation == SettingsMutationOperation.InstanceTenantSettings
                ? context.TenantFilterTenantId is { } tenant
                    && mutations.All(mutation => mutation.TenantId == tenant)
                : context.TenantFilterTenantId is null
                    && (operation != SettingsMutationOperation.InstanceSettings
                        || mutations.All(mutation => mutation.TenantId is null));
            bool allowed = targetsMatch && (tenantAction
                ? tenantAuthority is not null && await tenantAuthority.IsTenantAdministratorForCommitAsync(
                    subject.PrincipalId, context.TenantFilterTenantId!.Value, cancellationToken)
                : instanceAuthority is not null && await instanceAuthority.IsInstanceAdministratorForCommitAsync(
                    subject.PrincipalId, cancellationToken));
            if (!allowed)
                throw new UnauthorizedAccessException("Current resource settings administration is required.");
        }
        await RequireCurrentAuthorityAsync(token);
        if (subject is not null)
            finalizer!.Register(RequireCurrentAuthorityAsync, _ => { });
        if (mutations.IsEmpty)
            return new(true, null, []);
        string[] keys = EventResourceSettingMutationGuard.Keys.ToArray();
        Guid[] tenantIds = mutations.Where(mutation => mutation.TenantId.HasValue)
            .Select(mutation => mutation.TenantId!.Value).Distinct().ToArray();
        if (context.ChangeTracker.Entries<SystemSetting>().Any(entry => keys.Contains(entry.Entity.SettingKey)
                && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            || context.ChangeTracker.Entries<TenantSetting>().Any(entry => tenantIds.Contains(entry.Entity.TenantId)
                && keys.Contains(entry.Entity.SettingKey)
                && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            return Rejected("event_resource_policy_invalid");
        bool nativeImport = (RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql) && context.IsConfigurationInstanceImportOwner;
        var systems = mixed is not null
            ? mixed.Rows<SystemSetting>().Where(row => keys.Contains(row.SettingKey))
                .ToDictionary(row => row.SettingKey, StringComparer.Ordinal)
            : nativeImport
            ? await PostgresConfigurationImportEventResourceContract.ReadSystemsAsync(context, token)
            : await context.SystemSettings.AsNoTracking().Where(row => keys.Contains(row.SettingKey))
                .ToDictionaryAsync(row => row.SettingKey, token);
        var tenants = mixed is not null
            ? mixed.Rows<TenantSetting>().Where(row => tenantIds.Contains(row.TenantId) && keys.Contains(row.SettingKey))
                .ToDictionary(row => (row.TenantId, row.SettingKey))
            : nativeImport
            ? await PostgresConfigurationImportEventResourceContract.ReadTenantsAsync(context, tenantIds, token)
            : await context.TenantSettingOverrides
                .IgnoreTenantFilter(TenantFilterBypassReasons.EventResourceGovernanceMutation)
                .AsNoTracking().Where(row => tenantIds.Contains(row.TenantId) && keys.Contains(row.SettingKey))
                .ToDictionaryAsync(row => (row.TenantId, row.SettingKey), token);
        var originals = new Dictionary<(Guid? TenantId, string Key), string>();
        if (nativeImport)
        {
            foreach (SystemSetting row in systems.Values)
                originals.Add((null, row.SettingKey), PostgresConfigurationImportEventResourceContract.Capture(row));
            foreach (TenantSetting row in tenants.Values)
                originals.Add((row.TenantId, row.SettingKey), PostgresConfigurationImportEventResourceContract.Capture(row));
        }
        var writes = new List<(object Entity, EntityState State)>();
        var notifications = ImmutableArray.CreateBuilder<SettingChangedNotification>();
        DateTime now = mixed?.OccurredAt ?? clock.GetUtcNow().UtcDateTime;
        if (nativeImport)
            now = PostgresConfigurationImportEventResourceContract.Instant(now);
        foreach (var mutation in mutations.OrderBy(mutation => mutation.TenantId.HasValue))
        {
            SettingDefinition definition = SettingRegistry.Get(mutation.Key)!;
            if (mutation.TenantId is { } tenantId)
            {
                if (systems.GetValueOrDefault(mutation.Key)?.IsLocked == true)
                    return Rejected("setting_system_locked");
                tenants.TryGetValue((tenantId, mutation.Key), out TenantSetting? row);
                if (row is null && mutation.Kind == EventResourceSettingMutationKind.Remove) continue;
                if (mutation.Kind == EventResourceSettingMutationKind.SetLock)
                {
                    if (row is null) return Rejected("setting_override_not_found");
                    if (row.IsLocked == mutation.IsLocked) return Rejected("setting_state_conflict");
                }
                string? oldValue = row?.Value;
                bool created = row is null;
                row ??= new TenantSetting
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenantId,
                    Tenant = null!,
                    SettingKey = mutation.Key,
                    Value = definition.DefaultValue,
                    CreatedAt = now,
                    CreatedBy = actorUserId
                };
                if (mutation.Kind == EventResourceSettingMutationKind.Remove)
                    tenants.Remove((tenantId, mutation.Key));
                else
                {
                    row.Value = mutation.Value ?? row.Value;
                    row.IsLocked = mutation.IsLocked ?? row.IsLocked;
                    tenants[(tenantId, mutation.Key)] = row;
                }
                row.UpdatedAt = now;
                row.UpdatedBy = actorUserId;
                writes.Add((row, mutation.Kind == EventResourceSettingMutationKind.Remove ? EntityState.Deleted
                    : created ? EntityState.Added : EntityState.Modified));
                notifications.Add(new(mutation.Key, oldValue,
                    mutation.Kind == EventResourceSettingMutationKind.Remove ? null : row.Value,
                    SettingSource.TenantOverride, tenantId, actorUserId, now));
            }
            else
            {
                systems.TryGetValue(mutation.Key, out SystemSetting? row);
                if (row is null && mutation.Kind == EventResourceSettingMutationKind.Remove) continue;
                if (mutation.Kind == EventResourceSettingMutationKind.SetLock)
                {
                    if (row is null) return Rejected("setting_not_found");
                    if (row.IsLocked == mutation.IsLocked) return Rejected("setting_state_conflict");
                }
                string? oldValue = row?.Value;
                bool created = row is null;
                row ??= new SystemSetting
                {
                    Id = Guid.CreateVersion7(),
                    SettingKey = mutation.Key,
                    Value = definition.DefaultValue,
                    ValueType = definition.ValueType,
                    Category = definition.Category,
                    Description = definition.Description,
                    CreatedAt = now,
                    CreatedBy = actorUserId
                };
                if (mutation.Kind == EventResourceSettingMutationKind.Remove)
                    systems.Remove(mutation.Key);
                else
                {
                    row.Value = mutation.Value ?? row.Value;
                    row.IsLocked = mutation.IsLocked ?? row.IsLocked;
                    systems[mutation.Key] = row;
                }
                row.UpdatedAt = now;
                row.UpdatedBy = actorUserId;
                writes.Add((row, mutation.Kind == EventResourceSettingMutationKind.Remove ? EntityState.Deleted
                    : created ? EntityState.Added : EntityState.Modified));
                notifications.Add(new(mutation.Key, oldValue,
                    mutation.Kind == EventResourceSettingMutationKind.Remove ? null : row.Value,
                    row.IsLocked ? SettingSource.SystemLocked : SettingSource.SystemDefault, null, actorUserId, now));
            }
        }

        try
        {
            var instanceValues = keys.ToDictionary(key => key,
                key => HierarchicalSettingMerge.Resolve(key, systems)!.Value, StringComparer.Ordinal);
            EventResourceGovernancePolicy ceiling = EventResourceGovernancePolicyValues.Parse(instanceValues, long.MaxValue);
            foreach (var mutation in mutations.Where(mutation => mutation.TenantId.HasValue
                         && mutation.Kind == EventResourceSettingMutationKind.SetValue))
            {
                // Compare the raw requested value, not its clamped effective value. Unchanged old
                // overrides may exceed a newly tightened ceiling; access safely intersects those.
                var proposed = new Dictionary<string, string>(instanceValues, StringComparer.Ordinal)
                {
                    [mutation.Key] = mutation.Value!
                };
                if (!ceiling.IsNonWidening(EventResourceGovernancePolicyValues.Parse(proposed, long.MaxValue)))
                    return Rejected("event_resource_policy_widening");
            }
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return Rejected("event_resource_policy_invalid");
        }

        foreach (var entry in context.ChangeTracker.Entries<SystemSetting>()
                     .Where(entry => keys.Contains(entry.Entity.SettingKey)).ToArray())
            entry.State = EntityState.Detached;
        foreach (var entry in context.ChangeTracker.Entries<TenantSetting>()
                     .Where(entry => tenantIds.Contains(entry.Entity.TenantId) && keys.Contains(entry.Entity.SettingKey)).ToArray())
            entry.State = EntityState.Detached;
        if (mixed is not null)
        {
            await mixed.WriteResourceAsync(this, mutations, writes.ToImmutableArray(), token);
            return new(true, null, notifications.ToImmutable());
        }
        if (nativeImport)
        {
            foreach (var (entity, state) in writes)
            {
                (Guid? TenantId, string Key) identity = entity is TenantSetting tenant
                    ? (tenant.TenantId, tenant.SettingKey) : (null, ((SystemSetting)entity).SettingKey);
                await PostgresConfigurationImportEventResourceContract.WriteAsync(
                    context, entity, state, originals.GetValueOrDefault(identity), subject!,
                    instanceAuthority!, actorUserId, now, token);
                if (entity is SystemSetting system)
                    await ManagedTenantProvisioningPolicyProjection.RecordAsync(context, system.SettingKey,
                        state == EntityState.Deleted ? null : system, token);
            }
            return new(true, null, notifications.ToImmutable());
        }
        foreach (var (entity, state) in writes)
            context.Entry(entity).State = state;
        await context.SaveChangesAsync(token);
        foreach (var (entity, _) in writes)
            context.Entry(entity).State = EntityState.Detached;
        return new(true, null, notifications.ToImmutable());
    }

    private static bool IsValid(EventResourceSettingMutation mutation)
    {
        if (mutation is null || mutation.TenantId == Guid.Empty
            || !EventResourceSettingMutationGuard.Handles(mutation.Key) || !Enum.IsDefined(mutation.Kind))
            return false;
        SettingDefinition definition = SettingRegistry.Get(mutation.Key)!;
        if (mutation.TenantId.HasValue && definition.MaxScope < SettingScope.Tenant) return false;
        return mutation.Kind switch
        {
            EventResourceSettingMutationKind.Remove => mutation.Value is null && mutation.IsLocked is null,
            EventResourceSettingMutationKind.SetLock => mutation.Value is null && mutation.IsLocked.HasValue && definition.IsLockable,
            EventResourceSettingMutationKind.SetValue => mutation.Value is not null,
            _ => false
        };
    }

    private static EventResourceSettingsWriteResult Rejected(string code) => new(false, code, []);
}
