using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain;
using Explore.Domain.Constants;

namespace Explore.Application.Services.Federation;

public sealed class AtprotoPdsRecoveryPolicyResolver(
    ITenantRepository tenantRepository,
    ISystemSettingRepository systemSettingRepository,
    IAtprotoFederationGlobalReadRepository federationSettings)
{
    public async Task<AtprotoPdsRecoveryPolicy> ResolveAsync(CancellationToken cancellationToken)
    {
        SystemSetting? deploymentMode = await systemSettingRepository.GetByKey(
            GovernanceSettingKeys.Deployment.Mode,
            cancellationToken);
        string[] keys =
        [
            GovernanceSettingKeys.Federation.AtprotoEventsBackfillEnabled,
            GovernanceSettingKeys.Federation.AtprotoEventsBackfillMode
        ];
        IReadOnlyList<AtprotoFederationInstanceSetting> instanceSettings =
            await federationSettings.GetInstanceAsync(keys, cancellationToken);
        AtprotoFederationInstanceSetting? enabledSetting = instanceSettings.SingleOrDefault(
            setting => setting.SettingKey == GovernanceSettingKeys.Federation.AtprotoEventsBackfillEnabled);
        AtprotoFederationInstanceSetting? modeSetting = instanceSettings.SingleOrDefault(
            setting => setting.SettingKey == GovernanceSettingKeys.Federation.AtprotoEventsBackfillMode);
        IReadOnlyList<AtprotoFederationTenantSetting> tenantSettings =
            await federationSettings.GetOverridesAsync(keys, cancellationToken);
        IReadOnlyList<Tenant> tenants = await tenantRepository.GetActiveAsNoTrackingAsync(cancellationToken);

        bool instanceEnabled = SettingValueSerializer.DeserializeBool(enabledSetting?.Value, false);
        string instanceMode = NormalizeMode(SettingValueSerializer.DeserializeString(
            modeSetting?.Value,
            AtprotoFederationSettingGroup.DowntimeOnlyBackfillMode));
        bool isMultiTenant = string.Equals(
            SettingValueSerializer.DeserializeString(deploymentMode?.Value),
            "MultiTenant",
            StringComparison.OrdinalIgnoreCase);
        bool enabledOverridesApply = !isMultiTenant || enabledSetting?.IsLocked != true;
        bool modeOverridesApply = !isMultiTenant || modeSetting?.IsLocked != true;
        Dictionary<Guid, AtprotoFederationTenantSetting> enabledByTenant = tenantSettings
            .Where(setting => setting.SettingKey == GovernanceSettingKeys.Federation.AtprotoEventsBackfillEnabled)
            .ToDictionary(setting => setting.TenantId);
        Dictionary<Guid, AtprotoFederationTenantSetting> modeByTenant = tenantSettings
            .Where(setting => setting.SettingKey == GovernanceSettingKeys.Federation.AtprotoEventsBackfillMode)
            .ToDictionary(setting => setting.TenantId);

        var effective = tenants
            .Select(tenant => ResolveTenant(
                tenant.Id,
                instanceEnabled,
                instanceMode,
                enabledOverridesApply,
                modeOverridesApply,
                enabledByTenant,
                modeByTenant))
            .Where(value => value.IsEnabled)
            .OrderBy(value => value.TenantId)
            .ToArray();
        bool anyFull = effective.Any(value => value.Mode == AtprotoPdsRecoveryMode.Full);
        Guid[] recoveryTenantIds = effective
            .Where(value => !anyFull || value.Mode == AtprotoPdsRecoveryMode.Full)
            .Select(value => value.TenantId)
            .ToArray();
        string fingerprint = Hash(string.Join(
            '\n',
            effective.Select(value => $"{value.TenantId:N}:{(int)value.Mode}")));
        return new(
            effective.Length > 0,
            anyFull ? AtprotoPdsRecoveryMode.Full : AtprotoPdsRecoveryMode.DowntimeOnly,
            recoveryTenantIds,
            fingerprint);
    }

    private static EffectiveTenantRecovery ResolveTenant(
        Guid tenantId,
        bool instanceEnabled,
        string instanceMode,
        bool enabledOverridesApply,
        bool modeOverridesApply,
        IReadOnlyDictionary<Guid, AtprotoFederationTenantSetting> enabledByTenant,
        IReadOnlyDictionary<Guid, AtprotoFederationTenantSetting> modeByTenant)
    {
        bool enabled = enabledOverridesApply && enabledByTenant.TryGetValue(tenantId, out AtprotoFederationTenantSetting? enabledOverride)
            ? SettingValueSerializer.DeserializeBool(enabledOverride.Value, instanceEnabled)
            : instanceEnabled;
        string mode = modeOverridesApply && modeByTenant.TryGetValue(tenantId, out AtprotoFederationTenantSetting? modeOverride)
            ? NormalizeMode(SettingValueSerializer.DeserializeString(modeOverride.Value, instanceMode))
            : instanceMode;
        return new(
            tenantId,
            enabled,
            mode == AtprotoFederationSettingGroup.FullBackfillMode
                ? AtprotoPdsRecoveryMode.Full
                : AtprotoPdsRecoveryMode.DowntimeOnly);
    }

    private static string NormalizeMode(string mode) =>
        string.Equals(mode, AtprotoFederationSettingGroup.FullBackfillMode, StringComparison.OrdinalIgnoreCase)
            ? AtprotoFederationSettingGroup.FullBackfillMode
            : AtprotoFederationSettingGroup.DowntimeOnlyBackfillMode;

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record EffectiveTenantRecovery(
        Guid TenantId,
        bool IsEnabled,
        AtprotoPdsRecoveryMode Mode);
}
