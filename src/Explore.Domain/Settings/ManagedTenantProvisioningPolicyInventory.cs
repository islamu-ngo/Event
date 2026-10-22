using System.Collections.Immutable;
using Explore.Domain.Constants;
using Explore.Domain.Settings.Definitions;

namespace Explore.Domain.Settings;

/// <summary>Source-owned public policy keys; callers cannot widen the projection inventory.</summary>
public static class ManagedTenantProvisioningPolicyInventory
{
    public static ImmutableArray<string> ValueKeys { get; } =
    [
        GovernanceSettingKeys.Deployment.Mode,
        GovernanceSettingKeys.Tenants.WhiteLabelingEnabled,
        GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes,
        GovernanceSettingKeys.Domains.AllowTenantCustomDomain
    ];

    public static ImmutableArray<string> Keys { get; } = SettingRegistry.All
        .Where(definition => !definition.IsSensitive && definition.MinScope <= SettingScope.Tenant
            && definition.MaxScope >= SettingScope.Tenant)
        .Select(definition => definition.Key)
        .Concat(ValueKeys)
        .Concat([BrandingSettingDefinitions.DisplayName.Key, BrandingSettingDefinitions.LogoUrl.Key,
            BrandingSettingDefinitions.FaviconUrl.Key, BrandingSettingDefinitions.CustomCssUrl.Key])
        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();

    public static void RequireComplete(IEnumerable<ManagedTenantProvisioningPolicySetting> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var image = rows.ToArray();
        if (image.Length != Keys.Length
            || !image.Select(row => row.SettingKey).Order(StringComparer.Ordinal).SequenceEqual(Keys, StringComparer.Ordinal)
            || image.Select(row => row.Id).Distinct().Count() != image.Length)
            throw new InvalidOperationException("provisioning_policy_coverage_missing");
        foreach (var row in image) row.RequireValid();
    }
}
