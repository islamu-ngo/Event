
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
        TenantSetting? tenantSetting = null;
        tenantSettings?.TryGetValue(key, out tenantSetting);
        OrganizationSetting? organizationSetting = null;
        organizationSettings?.TryGetValue(key, out organizationSetting);
        GroupSetting? groupSetting = null;
        groupSettings?.TryGetValue(key, out groupSetting);
        UserPreference? userPreference = null;
        userPreferences?.TryGetValue(key, out userPreference);
        systemSettings.TryGetValue(GovernanceSettingKeys.TenantDelegation.LockSmtp, out var delegation);

        return Resolve(key, new SettingMergeFacts(
            systemSetting is null ? null : new SystemSettingFacts(
                systemSetting.Value, systemSetting.ValueType, systemSetting.IsLocked,
                systemSetting.Description, systemSetting.Category, systemSetting.AllowedValues),
            tenantSetting is null ? null : new TenantSettingFacts(tenantSetting.Value, tenantSetting.IsLocked),
            organizationSetting?.Value,
            groupSetting?.Value,
            userPreference?.Value,
            tenantSettings is not null,
            delegation?.Value));
    }

    public static ResolvedSetting? Resolve(string key, SettingMergeFacts facts)
    {
        var systemSetting = facts.System;
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
        var smtpDelegationLocked = facts.HasTenantContext
            && EmailSettingDefinitions.All.Any(setting => setting.Key == key)
            && SettingValueSerializer.DeserializeBool(facts.SmtpDelegationValue, true);
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
            && facts.Tenant is { } tenantOverride)
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
            && facts.Organization is { } organizationValue)
        {
            effectiveValue = organizationValue;
            source = SettingSource.OrganizationOverride;
        }

        if (AllowsScope(definition, SettingScope.Group)
            && facts.Group is { } groupValue)
        {
            effectiveValue = groupValue;
            source = SettingSource.GroupOverride;
        }

        if (facts.User is { } userValue)
        {
            var maxScope = definition?.MaxScope ?? SettingScope.Tenant;
            if (maxScope >= SettingScope.User)
            {
                effectiveValue = userValue;
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
