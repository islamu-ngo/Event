namespace Explore.Application.Services.Federation;

using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;

public sealed class AtprotoJetstreamTenantPresentationResolver(
    ITenantRepository tenantRepository,
    ISystemSettingRepository systemSettingRepository,
    IAtprotoFederationGlobalReadRepository federationSettings)
{
    public async Task<IReadOnlyList<Guid>> ResolveEnabledTenantIdsAsync(CancellationToken cancellationToken)
    {
        string[] keys = [GovernanceSettingKeys.Federation.AtprotoEventsEnabled];
        IReadOnlyList<AtprotoFederationInstanceSetting> instanceSettings =
            await federationSettings.GetInstanceAsync(keys, cancellationToken);
        AtprotoFederationInstanceSetting? capability = instanceSettings.SingleOrDefault();
        SystemSetting? deploymentMode = await systemSettingRepository.GetByKey(
            GovernanceSettingKeys.Deployment.Mode,
            cancellationToken);
        IReadOnlyList<AtprotoFederationTenantSetting> tenantSettings =
            await federationSettings.GetOverridesAsync(keys, cancellationToken);
        IReadOnlyList<Tenant> tenants = await tenantRepository.GetActiveAsNoTrackingAsync(cancellationToken);

        bool instanceEnabled = SettingValueSerializer.DeserializeBool(capability?.Value, false);
        bool isMultiTenant = string.Equals(
            SettingValueSerializer.DeserializeString(deploymentMode?.Value),
            "MultiTenant",
            StringComparison.OrdinalIgnoreCase);
        bool tenantOverridesApply = capability is not null && (!isMultiTenant || !capability.IsLocked);
        Dictionary<Guid, AtprotoFederationTenantSetting> overrides = tenantSettings
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
        IReadOnlyDictionary<Guid, AtprotoFederationTenantSetting> overrides)
    {
        if (!tenantOverridesApply || !overrides.TryGetValue(tenantId, out AtprotoFederationTenantSetting? tenantSetting))
        {
            return instanceEnabled;
        }

        return SettingValueSerializer.DeserializeBool(tenantSetting.Value, instanceEnabled);
    }
}
