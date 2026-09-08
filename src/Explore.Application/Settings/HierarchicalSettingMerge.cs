
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;

namespace Explore.Application.Settings;

public static class HierarchicalSettingMerge
{
    public static ResolvedSetting? Resolve(
        string key,
        IReadOnlyDictionary<string, SystemSetting> systemSettings,
        IReadOnlyDictionary<string, TenantSetting>? tenantSettings = null,
        IReadOnlyDictionary<string, OrganizationSetting>? organizationSettings = null,
        IReadOnlyDictionary<string, GroupSetting>? groupSettings = null,
        IReadOnlyDictionary<string, UserPreference>? userPreferences = null)
    {
        systemSettings.TryGetValue(key, out var systemSetting);
        var definition = SettingRegistry.Get(key);

        if (systemSetting is null && definition is null)
            return null;

        var effectiveValue = systemSetting?.Value ?? definition?.DefaultValue ?? "";
        var valueType = systemSetting?.ValueType ?? definition?.ValueType ?? SettingValueType.String;
        var description = systemSetting?.Description ?? definition?.Description;
        var category = systemSetting?.Category ?? definition?.Category;
        var allowedValues = systemSetting?.AllowedValues;

        // Instance locks stop all overrides in both single-tenant and multi-tenant deployments.
        // A non-null tenant map denotes tenant resolution even when no overrides exist.
        var smtpDelegationLocked = tenantSettings is not null
            && EmailSettingDefinitions.All.Any(setting => setting.Key == key)
            && (!systemSettings.TryGetValue(GovernanceSettingKeys.TenantDelegation.LockSmtp, out var delegation)
                || SettingValueSerializer.DeserializeBool(delegation.Value, true));
        var isInstanceLocked = systemSetting?.IsLocked == true || smtpDelegationLocked;
        if (isInstanceLocked)
        {
            return new ResolvedSetting
            {
                Key = key,
                Value = effectiveValue,
                ValueType = valueType,
                Source = SettingSource.SystemLocked,
                IsLocked = true,
                Description = description,
                Category = category,
                AllowedValues = allowedValues
            };
        }

        var source = SettingSource.SystemDefault;
        var isTenantLocked = false;
        if (AllowsScope(definition, SettingScope.Tenant)
            && tenantSettings is not null
            && tenantSettings.TryGetValue(key, out var tenantOverride))
        {
            effectiveValue = tenantOverride.Value;
            source = SettingSource.TenantOverride;
            isTenantLocked = tenantOverride.IsLocked;
        }

        // Tenant locks stop child overrides without deleting their stored values.
        if (isTenantLocked)
        {
            return new ResolvedSetting
            {
                Key = key,
                Value = effectiveValue,
                ValueType = valueType,
                Source = SettingSource.TenantLocked,
                IsLocked = true,
                Description = description,
                Category = category,
                AllowedValues = allowedValues
            };
        }

        if (AllowsScope(definition, SettingScope.Organization)
            && organizationSettings is not null
            && organizationSettings.TryGetValue(key, out var orgOverride))
        {
            effectiveValue = orgOverride.Value;
            source = SettingSource.OrganizationOverride;
        }

        if (AllowsScope(definition, SettingScope.Group)
            && groupSettings is not null
            && groupSettings.TryGetValue(key, out var groupOverride))
        {
            effectiveValue = groupOverride.Value;
            source = SettingSource.GroupOverride;
        }

        if (userPreferences is not null && userPreferences.TryGetValue(key, out var userPref))
        {
            var maxScope = definition?.MaxScope ?? SettingScope.Tenant;
            if (maxScope >= SettingScope.User)
            {
                effectiveValue = userPref.Value;
                source = SettingSource.UserPreference;
            }
        }

        return new ResolvedSetting
        {
            Key = key,
            Value = effectiveValue,
            ValueType = valueType,
            Source = source,
            IsLocked = false,
            Description = description,
            Category = category,
            AllowedValues = allowedValues
        };
    }

    private static bool AllowsScope(SettingDefinition? definition, SettingScope scope) =>
        definition is null || scope >= definition.MinScope && scope <= definition.MaxScope;
}
