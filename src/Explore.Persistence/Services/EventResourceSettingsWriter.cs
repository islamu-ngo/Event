using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Notifications;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Domain.ValueObjects;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

public sealed class EventResourceSettingsWriter(
    ExploreDbContext context,
    ISettingMutationLock mutationLock,
    IUnitOfWork unitOfWork) : IEventResourceSettingsWriter
{
    public Task<EventResourceSettingsWriteResult> ApplyAsync(
        ImmutableArray<EventResourceSettingMutation> mutations,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (mutations.IsDefault || mutations.Any(mutation => !IsValid(mutation))
            || mutations.Select(mutation => (mutation.TenantId, mutation.Key)).Distinct().Count() != mutations.Length)
            return Task.FromResult(Rejected("event_resource_policy_invalid"));
        if (mutations.IsEmpty)
            return Task.FromResult(new EventResourceSettingsWriteResult(true, null, []));
        if (context.Database.CurrentTransaction is null)
            return mutationLock.ExecuteOrderedGroupsAsync([EventResourceSettingMutationGuard.Keys],
                token => unitOfWork.ExecuteSerializableAsync(
                    inner => ApplyInsideTransactionAsync(mutations, actorUserId, inner), token), cancellationToken);
        return mutationLock.ExecuteManyAsync(EventResourceSettingMutationGuard.Keys,
            token => ApplyInsideTransactionAsync(mutations, actorUserId, token), cancellationToken);
    }

    private async Task<EventResourceSettingsWriteResult> ApplyInsideTransactionAsync(
        ImmutableArray<EventResourceSettingMutation> mutations, Guid? actorUserId, CancellationToken token)
    {
        string[] keys = EventResourceSettingMutationGuard.Keys.ToArray();
        Guid[] tenantIds = mutations.Where(mutation => mutation.TenantId.HasValue)
            .Select(mutation => mutation.TenantId!.Value).Distinct().ToArray();
        if (context.ChangeTracker.Entries<SystemSetting>().Any(entry => keys.Contains(entry.Entity.SettingKey)
                && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            || context.ChangeTracker.Entries<TenantSetting>().Any(entry => tenantIds.Contains(entry.Entity.TenantId)
                && keys.Contains(entry.Entity.SettingKey)
                && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            return Rejected("event_resource_policy_invalid");
        var systems = await context.SystemSettings.AsNoTracking().Where(row => keys.Contains(row.SettingKey))
            .ToDictionaryAsync(row => row.SettingKey, token);
        var tenants = await context.TenantSettingOverrides
            .IgnoreTenantFilter(TenantFilterBypassReasons.EventResourceGovernanceMutation)
            .AsNoTracking().Where(row => tenantIds.Contains(row.TenantId) && keys.Contains(row.SettingKey))
            .ToDictionaryAsync(row => (row.TenantId, row.SettingKey), token);
        var writes = new List<(object Entity, EntityState State)>();
        var notifications = ImmutableArray.CreateBuilder<SettingChangedNotification>();
        DateTime now = DateTime.UtcNow;
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
                    Id = Guid.CreateVersion7(), TenantId = tenantId, Tenant = null!,
                    SettingKey = mutation.Key, Value = definition.DefaultValue,
                    CreatedAt = now, CreatedBy = actorUserId
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
                    Id = Guid.CreateVersion7(), SettingKey = mutation.Key, Value = definition.DefaultValue,
                    ValueType = definition.ValueType, Category = definition.Category, Description = definition.Description,
                    CreatedAt = now, CreatedBy = actorUserId
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
