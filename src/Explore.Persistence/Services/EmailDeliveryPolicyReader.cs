// ABOUTME: Reads authoritative non-secret email policy through the shared settings merge inside caller-owned transactions.
// ABOUTME: Avoids process caches, credential resolution, and network I/O during final delivery admission.

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

internal sealed record EmailDeliveryPolicySet(
    EmailDeliveryPolicySnapshot Instance,
    IReadOnlyDictionary<Guid, EmailDeliveryPolicySnapshot> Tenants);

internal sealed record EmailDeliveryPolicyChange(
    EmailDeliveryPolicySet Before,
    EmailDeliveryPolicySet After,
    bool IsLocked);

internal static class EmailDeliveryPolicyReader
{
    private static readonly string[] Keys = [.. EmailDeliverySettingKeys.All];

    public static async Task<EmailDeliveryPolicySnapshot> ReadAsync(
        ExploreDbContext context,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        Dictionary<string, SystemSetting> systemSettings = await ReadSystemSettingsAsync(context, cancellationToken);
        Dictionary<string, TenantSetting>? tenantSettings = tenantId is { } id
            ? await context.TenantSettingOverrides
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .AsNoTracking()
                .Where(setting => setting.TenantId == id && Keys.Contains(setting.SettingKey))
                .ToDictionaryAsync(setting => setting.SettingKey, cancellationToken)
            : null;

        Dictionary<string, ResolvedSetting> instance = Resolve(Keys, systemSettings, tenantSettings: null);
        Dictionary<string, ResolvedSetting> effective = tenantId.HasValue
            ? Resolve(Keys, systemSettings, tenantSettings)
            : instance;
        return EmailDeliveryPolicySnapshot.Evaluate(instance, effective, tenantId);
    }

    public static async Task<EmailDeliveryPolicySet> ReadAllAsync(
        ExploreDbContext context, CancellationToken cancellationToken)
    {
        var systemSettings = await ReadSystemSettingsAsync(context, cancellationToken);
        var tenantSettings = await ReadTenantSettingsAsync(context, tenantId: null, cancellationToken);
        return EvaluateAll(systemSettings, tenantSettings);
    }

    public static async Task<EmailDeliveryPolicyChange?> ReadDisableAsync(
        ExploreDbContext context, Guid? tenantId, CancellationToken cancellationToken)
    {
        var systemSettings = await ReadSystemSettingsAsync(context, cancellationToken);
        var tenantSettings = await ReadTenantSettingsAsync(context, tenantId, cancellationToken);
        if (tenantId is { } id && !tenantSettings.ContainsKey(id))
            return null;

        // Both policies use this one materialized setting and membership view, even under ReadCommitted.
        var before = EvaluateAll(systemSettings, tenantSettings);
        var selectedSettings = tenantId is { } selectedId ? tenantSettings[selectedId] : null;
        bool isLocked = tenantId.HasValue && HierarchicalSettingMerge.Resolve(
            GovernanceSettingKeys.Email.DeliveryEnabled, systemSettings, selectedSettings)?.Source == SettingSource.SystemLocked;
        ApplyDisable(systemSettings, selectedSettings, tenantId);
        return new(Before: before, After: EvaluateAll(systemSettings, tenantSettings), IsLocked: isLocked);
    }

    internal static async Task<Dictionary<Guid, Dictionary<string, TenantSetting>>> ReadTenantSettingsAsync(
        ExploreDbContext context, Guid? tenantId, CancellationToken cancellationToken)
    {
        var tenantIds = await context.Tenants.AsNoTracking()
            .Where(tenant => tenantId == null || tenant.Id == tenantId)
            .Select(tenant => tenant.Id).ToListAsync(cancellationToken);
        var rows = await context.TenantSettingOverrides
            .IgnoreTenantFilter(tenantId.HasValue
                ? TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate
                : TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
            .AsNoTracking()
            .Where(setting => (tenantId == null || setting.TenantId == tenantId) && Keys.Contains(setting.SettingKey)
                && context.Tenants.Any(tenant => tenant.Id == setting.TenantId))
            .ToListAsync(cancellationToken);
        var overrides = rows.GroupBy(setting => setting.TenantId)
            .ToDictionary(group => group.Key,
                group => group.ToDictionary(setting => setting.SettingKey, StringComparer.Ordinal));
        return tenantIds.ToDictionary(id => id, id => overrides.GetValueOrDefault(id) ?? []);
    }

    internal static EmailDeliveryPolicySet EvaluateAll(
        Dictionary<string, SystemSetting> systemSettings,
        Dictionary<Guid, Dictionary<string, TenantSetting>> tenantSettings)
    {
        var instance = Resolve(Keys, systemSettings, tenantSettings: null);
        var instancePolicy = EmailDeliveryPolicySnapshot.Evaluate(instance, instance, tenantId: null);
        // ponytail: instance edits materialize all tenant policies; project affected scopes in SQL if scale requires it.
        var tenants = tenantSettings.ToDictionary(pair => pair.Key, pair =>
            EmailDeliveryPolicySnapshot.Evaluate(instance,
                Resolve(Keys, systemSettings, pair.Value), pair.Key));
        return new EmailDeliveryPolicySet(Instance: instancePolicy, Tenants: tenants);
    }

    private static void ApplyDisable(Dictionary<string, SystemSetting> systemSettings,
        Dictionary<string, TenantSetting>? tenantSettings, Guid? tenantId)
    {
        const string key = GovernanceSettingKeys.Email.DeliveryEnabled;
        // These are detached read models. Preserve lock flags while evaluating the hypothetical value.
        if (tenantId is { } id)
        {
            if (tenantSettings!.TryGetValue(key, out var setting))
                setting.Value = "false";
            else
                tenantSettings[key] = new TenantSetting { TenantId = id, Tenant = null!, SettingKey = key, Value = "false" };
        }
        else if (systemSettings.TryGetValue(key, out var setting))
            setting.Value = "false";
        else
            systemSettings[key] = new SystemSetting { SettingKey = key, Value = "false", ValueType = SettingValueType.Boolean };
    }

    internal static Task<Dictionary<string, SystemSetting>> ReadSystemSettingsAsync(
        ExploreDbContext context, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Email policy reads require an active transaction.");

        return context.SystemSettings.AsNoTracking()
            .Where(setting => Keys.Contains(setting.SettingKey))
            .ToDictionaryAsync(setting => setting.SettingKey, cancellationToken);
    }

    private static Dictionary<string, ResolvedSetting> Resolve(
        IEnumerable<string> keys,
        IReadOnlyDictionary<string, SystemSetting> systemSettings,
        IReadOnlyDictionary<string, TenantSetting>? tenantSettings) =>
        keys.Select(key => HierarchicalSettingMerge.Resolve(key, systemSettings, tenantSettings))
            .OfType<ResolvedSetting>()
            .ToDictionary(setting => setting.Key, StringComparer.Ordinal);
}
