namespace Explore.Application.Features.ConfigurationManifest.Application;

using System.Collections.Immutable;
using Explore.Application.Features.PaidEventPolicies;
using Explore.Application.Features.ConfigurationManifest.Compilation;
using Explore.Application.Features.Tenants;
using Explore.Application.Settings;

public static class ConfigurationManifestLockKeys
{
    public const string InstanceManifest = "!configuration-manifest.instance";

    public static ImmutableArray<ImmutableArray<string>> CompileOrderedGroups(
        ConfigurationManifestApplyPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var instanceResources = new HashSet<string>(StringComparer.Ordinal);
        var tenantResources = new HashSet<string>(StringComparer.Ordinal);
        foreach (ConfigurationManifestSettingWrite setting
                 in plan.Instance.GuardedSettings)
        {
            instanceResources.Add(setting.Key);
        }

        foreach (ConfigurationManifestSettingWrite setting
                 in plan.Instance.UnguardedSettings)
        {
            instanceResources.Add(setting.Key);
        }

        if (plan.Instance.PaidEventPolicy is not null)
        {
            instanceResources.Add(PaidEventPolicyMutationLockKeys.Instance);
        }

        if (!plan.Instance.GuardedSettings.IsEmpty
            || plan.Tenants.Any(tenant => !tenant.GuardedSettings.IsEmpty))
        {
            instanceResources.UnionWith(PublicationPolicySettingKeys.All);
        }

        instanceResources.UnionWith(TenantBrandingGovernanceMutationLockKeys.All);
        foreach (ConfigurationManifestTenantPlan tenant in plan.Tenants)
        {
            tenantResources.Add(TenantMutationLockKeys.ForSlug(tenant.Slug));
            foreach (ConfigurationManifestSettingWrite setting
                     in tenant.GuardedSettings)
            {
                tenantResources.Add(setting.Key);
            }

            foreach (ConfigurationManifestSettingWrite setting
                     in tenant.UnguardedSettings)
            {
                tenantResources.Add(setting.Key);
            }

            if (tenant.PaidEventPolicy is not null)
            {
                tenantResources.Add(PaidEventPolicyMutationLockKeys.ForTenant(
                    tenant.PlannedTenantId));
            }
        }

        tenantResources.ExceptWith(instanceResources);
        return
        [
            [InstanceManifest],
            [.. instanceResources.Order(StringComparer.Ordinal)],
            [.. tenantResources.Order(StringComparer.Ordinal)]
        ];
    }

    public static ImmutableArray<string> Compile(ConfigurationManifestApplyPlan plan)
    {
        return CompileOrderedGroups(plan)
            .SelectMany(group => group)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
    }
}
