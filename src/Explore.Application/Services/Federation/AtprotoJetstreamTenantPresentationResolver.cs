namespace Explore.Application.Services.Federation;

using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;

public sealed class AtprotoJetstreamTenantPresentationResolver(
    ITenantRepository tenantRepository,
    ISystemSettingRepository systemSettingRepository,
    ITenantSettingRepository tenantSettingRepository)
{
    public async Task<IReadOnlyList<Guid>> ResolveEnabledTenantIdsAsync(CancellationToken cancellationToken)
    {
        SystemSetting? capability = await systemSettingRepository.GetByKey(
            GovernanceSettingKeys.Federation.AtprotoEventsEnabled,
            cancellationToken);
        SystemSetting? deploymentMode = await systemSettingRepository.GetByKey(
            GovernanceSettingKeys.Deployment.Mode,
            cancellationToken);
        List<TenantSetting> tenantSettings = await tenantSettingRepository.GetByKeyAcrossTenants(
            GovernanceSettingKeys.Federation.AtprotoEventsEnabled,
            cancellationToken);
        IReadOnlyList<Tenant> tenants = await tenantRepository.GetActiveAsNoTrackingAsync(cancellationToken);

        bool instanceEnabled = SettingValueSerializer.DeserializeBool(capability?.Value, false);
        bool isMultiTenant = string.Equals(
            SettingValueSerializer.DeserializeString(deploymentMode?.Value),
            "MultiTenant",
            StringComparison.OrdinalIgnoreCase);
        bool tenantOverridesApply = capability is not null && (!isMultiTenant || !capability.IsLocked);
        Dictionary<Guid, TenantSetting> overrides = tenantSettings
            .ToDictionary(setting => setting.TenantId);

        return tenants
            .Where(tenant => IsEnabled(tenant.Id, instanceEnabled, tenantOverridesApply, overrides))
            .Select(tenant => tenant.Id)
            .Order()
            .ToArray();
    }

    private static bool IsEnabled(
        Guid tenantId,
        bool instanceEnabled,
        bool tenantOverridesApply,
        IReadOnlyDictionary<Guid, TenantSetting> overrides)
    {
        if (!tenantOverridesApply || !overrides.TryGetValue(tenantId, out TenantSetting? tenantSetting))
        {
            return instanceEnabled;
        }

        return SettingValueSerializer.DeserializeBool(tenantSetting.Value, instanceEnabled);
    }
}
