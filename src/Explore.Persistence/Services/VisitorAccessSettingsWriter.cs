// ABOUTME: Validates complete visitor/provider mutations before atomic persistence under a pretransaction lease.
// ABOUTME: Preserves usable public signup for every affected AccountRequired configuration without rewriting events.

using System.Collections.Immutable;
using System.Data;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Models;
using Explore.Application.Notifications;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;

namespace Explore.Persistence.Services;

public sealed class VisitorAccessSettingsWriter(
    ExploreDbContext context,
    ISettingMutationLock mutationLock,
    IUnitOfWork unitOfWork,
    IEventParticipationConfigurationRepository participation,
    IConfiguration configuration) : IVisitorAccessSettingsWriter
{
    public Task<VisitorAccessSettingsWriteResult> ApplyAsync(
        ImmutableArray<VisitorAccessSettingMutation> mutations,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (mutations.IsDefault || mutations.Any(mutation => !IsValid(mutation))
            || mutations.Select(mutation => (mutation.TenantId, mutation.Key)).Distinct().Count() != mutations.Length)
            return Task.FromResult(Rejected("visitor_access_policy_invalid"));
        if (mutations.IsEmpty)
            return Task.FromResult(new VisitorAccessSettingsWriteResult(true, null, []));
        if (context.Database.CurrentTransaction is null)
            return mutationLock.ExecuteOrderedGroupsAsync([VisitorAccessCapabilityResolver.AuthoritySettingKeys],
                token => unitOfWork.ExecuteSerializableAsync(
                    innerToken => ApplyInsideTransactionAsync(mutations, actorUserId, innerToken), token), cancellationToken);
        if (context.Database.CurrentTransaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Visitor policy writes require a serializable transaction.");
        return mutationLock.ExecuteManyAsync(VisitorAccessCapabilityResolver.AuthoritySettingKeys,
            token => ApplyInsideTransactionAsync(mutations, actorUserId, token), cancellationToken);
    }

    private async Task<VisitorAccessSettingsWriteResult> ApplyInsideTransactionAsync(
        ImmutableArray<VisitorAccessSettingMutation> mutations, Guid? actorUserId, CancellationToken token)
    {
        string[] keys = VisitorAccessCapabilityResolver.AuthoritySettingKeys.ToArray();
        if (context.ChangeTracker.Entries<SystemSetting>().Any(entry => keys.Contains(entry.Entity.SettingKey)
                && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            || context.ChangeTracker.Entries<TenantSetting>().Any(entry => keys.Contains(entry.Entity.SettingKey)
                && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            return Rejected("visitor_access_policy_invalid");
        var systems = await context.SystemSettings.AsNoTracking()
            .Where(row => keys.Contains(row.SettingKey)).ToDictionaryAsync(row => row.SettingKey, token);
        var tenants = await context.TenantSettingOverrides
            .IgnoreTenantFilter(TenantFilterBypassReasons.VisitorPolicyAuthorityInheritanceSafetyRead)
            .AsNoTracking().Where(row => keys.Contains(row.SettingKey)).ToListAsync(token);
        var tenantRows = tenants.ToDictionary(row => (row.TenantId, row.SettingKey));
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
                tenantRows.TryGetValue((tenantId, mutation.Key), out TenantSetting? row);
                if (row is null && mutation.Kind == VisitorAccessSettingMutationKind.Remove)
                    continue;
                if (mutation.Kind == VisitorAccessSettingMutationKind.SetLock)
                {
                    if (row is null) return Rejected("setting_override_not_found");
                    if (row.IsLocked == mutation.IsLocked) return Rejected("setting_state_conflict");
                }
                string? oldValue = row?.Value;
                bool created = row is null;
                row ??= new TenantSetting { Id = Guid.CreateVersion7(), TenantId = tenantId, Tenant = null!,
                    SettingKey = mutation.Key, Value = definition.DefaultValue, CreatedAt = now, CreatedBy = actorUserId };
                if (mutation.Kind == VisitorAccessSettingMutationKind.Remove)
                    tenantRows.Remove((tenantId, mutation.Key));
                else
                {
                    row.Value = mutation.Value ?? row.Value;
                    row.IsLocked = mutation.IsLocked ?? row.IsLocked;
                    tenantRows[(tenantId, mutation.Key)] = row;
                }
                row.UpdatedAt = now;
                row.UpdatedBy = actorUserId;
                writes.Add((row, mutation.Kind == VisitorAccessSettingMutationKind.Remove ? EntityState.Deleted
                    : created ? EntityState.Added : EntityState.Modified));
                notifications.Add(new(mutation.Key, oldValue,
                    mutation.Kind == VisitorAccessSettingMutationKind.Remove ? null : row.Value,
                    SettingSource.TenantOverride, tenantId, actorUserId, now));
            }
            else
            {
                systems.TryGetValue(mutation.Key, out SystemSetting? row);
                if (row is null && mutation.Kind == VisitorAccessSettingMutationKind.Remove)
                    continue;
                if (mutation.Kind == VisitorAccessSettingMutationKind.SetLock && row is null)
                    return Rejected("setting_not_found");
                string? oldValue = row?.Value;
                bool created = row is null;
                row ??= new SystemSetting { Id = Guid.CreateVersion7(), SettingKey = mutation.Key,
                    Value = definition.DefaultValue, ValueType = definition.ValueType, Category = definition.Category,
                    Description = definition.Description, CreatedAt = now, CreatedBy = actorUserId };
                if (mutation.Kind == VisitorAccessSettingMutationKind.Remove)
                    systems.Remove(mutation.Key);
                else
                {
                    row.Value = mutation.Value ?? row.Value;
                    row.IsLocked = mutation.IsLocked ?? row.IsLocked;
                    systems[mutation.Key] = row;
                }
                row.UpdatedAt = now;
                row.UpdatedBy = actorUserId;
                writes.Add((row, mutation.Kind == VisitorAccessSettingMutationKind.Remove ? EntityState.Deleted
                    : created ? EntityState.Added : EntityState.Modified));
                notifications.Add(new(mutation.Key, oldValue,
                    mutation.Kind == VisitorAccessSettingMutationKind.Remove ? null : row.Value,
                    row.IsLocked ? SettingSource.SystemLocked : SettingSource.SystemDefault, null, actorUserId, now));
            }
        }

        bool instanceChanged = mutations.Any(mutation => mutation.TenantId is null);
        var affectedTenantIds = mutations.Where(mutation => mutation.TenantId.HasValue)
            .Select(mutation => mutation.TenantId!.Value).ToHashSet();
        var affected = await participation.GetAccountRequiredAsync(
            !instanceChanged && affectedTenantIds.Count == 1 ? affectedTenantIds.Single() : null, token);
        IReadOnlyList<VisitorAccessProviderState> providers;
        try
        {
            providers = AuthProviderConfigurationService.ProjectVisitorProviders(systems, configuration);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return Rejected("visitor_access_policy_invalid");
        }
        foreach (Guid tenantId in affected.Select(item => item.TenantId).Distinct()
                     .Where(id => instanceChanged || affectedTenantIds.Contains(id)))
        {
            VisitorAccessMode mode = VisitorAccessCapabilityResolver.ResolveMode(
                systems.GetValueOrDefault(GovernanceSettingKeys.PublicExperience.VisitorAccessMode),
                tenantRows.GetValueOrDefault((tenantId, GovernanceSettingKeys.PublicExperience.VisitorAccessMode)));
            VisitorAccessCapability capability = VisitorAccessCapabilityResolver.EvaluateProposedState(new VisitorAccessPolicyState(mode, providers));
            if (!capability.AllowsAccountRequiredParticipation)
                return Rejected("visitor_access_account_required_conflict");
        }

        // No persistent or tracked state is changed until every affected scope has passed.
        foreach (var entry in context.ChangeTracker.Entries<SystemSetting>()
                     .Where(entry => keys.Contains(entry.Entity.SettingKey)).ToArray())
            entry.State = EntityState.Detached;
        foreach (var entry in context.ChangeTracker.Entries<TenantSetting>()
                     .Where(entry => keys.Contains(entry.Entity.SettingKey)).ToArray())
            entry.State = EntityState.Detached;
        foreach (var (entity, state) in writes)
            context.Entry(entity).State = state;
        await context.SaveChangesAsync(token);
        foreach (var (entity, _) in writes)
            context.Entry(entity).State = EntityState.Detached;
        return new(true, null, notifications.ToImmutable());
    }

    private static bool IsValid(VisitorAccessSettingMutation mutation)
    {
        if (mutation is null || mutation.TenantId == Guid.Empty
            || !VisitorAccessCapabilityResolver.AuthoritySettingKeys.Contains(mutation.Key)
            || !Enum.IsDefined(mutation.Kind))
            return false;
        SettingDefinition definition = SettingRegistry.Get(mutation.Key)!;
        if (mutation.TenantId.HasValue && definition.MaxScope < SettingScope.Tenant)
            return false;
        if (mutation.Kind == VisitorAccessSettingMutationKind.Remove)
            return mutation.Value is null && mutation.IsLocked is null;
        if (mutation.Kind == VisitorAccessSettingMutationKind.SetLock)
            return mutation.Value is null && mutation.IsLocked.HasValue && definition.IsLockable;
        if (mutation.Value is null)
            return false;
        try
        {
            using JsonDocument json = JsonDocument.Parse(mutation.Value);
            return definition.ValueType switch
            {
                SettingValueType.Boolean => json.RootElement.ValueKind is JsonValueKind.True or JsonValueKind.False,
                SettingValueType.Integer => json.RootElement.ValueKind == JsonValueKind.Number && json.RootElement.TryGetInt32(out _),
                SettingValueType.String => json.RootElement.ValueKind == JsonValueKind.String
                    && (definition.AllowedValues is null || definition.AllowedValues.Contains(json.RootElement.GetString()!)),
                _ => false
            };
        }
        catch (JsonException) { return false; }
    }

    private static VisitorAccessSettingsWriteResult Rejected(string code) => new(false, code, []);
}
