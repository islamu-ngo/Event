namespace Explore.Application.Settings.Groups;

using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Constants;

public class TenantDelegationSettingGroup : ISettingGroup
{
    public bool LockSmtp { get; private set; } = true;
    public bool LockStorage { get; private set; } = true;
    public bool LockAnalytics { get; private set; } = true;
    public bool LockAiAssistant { get; private set; } = true;
    public bool LockReportingProviders { get; private set; } = true;
    public bool LockTenantOspreyProvider { get; private set; } = true;
    public bool LockTenantCoopProvider { get; private set; } = true;
    public bool LockMcp { get; private set; } = true;
    public bool LockMcpLegacySse { get; private set; } = true;

    public static IEnumerable<string> SettingKeys =>
    [
        GovernanceSettingKeys.TenantDelegation.LockSmtp,
        GovernanceSettingKeys.TenantDelegation.LockStorage,
        GovernanceSettingKeys.TenantDelegation.LockAnalytics,
        GovernanceSettingKeys.TenantDelegation.LockAiAssistant,
        GovernanceSettingKeys.TenantDelegation.LockReportingProviders,
        GovernanceSettingKeys.TenantDelegation.LockTenantOspreyProvider,
        GovernanceSettingKeys.TenantDelegation.LockTenantCoopProvider,
        GovernanceSettingKeys.TenantDelegation.LockMcp,
        GovernanceSettingKeys.TenantDelegation.LockMcpLegacySse
    ];

    public void Populate(IReadOnlyDictionary<string, ResolvedSetting> settings)
    {
        if (settings.TryGetValue(GovernanceSettingKeys.TenantDelegation.LockSmtp, out var smtp))
            LockSmtp = SettingValueSerializer.Deserialize(smtp.Value, true);
        if (settings.TryGetValue(GovernanceSettingKeys.TenantDelegation.LockStorage, out var storage))
            LockStorage = SettingValueSerializer.Deserialize(storage.Value, true);
        if (settings.TryGetValue(GovernanceSettingKeys.TenantDelegation.LockAnalytics, out var analytics))
            LockAnalytics = SettingValueSerializer.Deserialize(analytics.Value, true);
        if (settings.TryGetValue(GovernanceSettingKeys.TenantDelegation.LockAiAssistant, out var aiAssistant))
            LockAiAssistant = SettingValueSerializer.Deserialize(aiAssistant.Value, true);
        if (settings.TryGetValue(GovernanceSettingKeys.TenantDelegation.LockReportingProviders, out var reporting))
            LockReportingProviders = SettingValueSerializer.Deserialize(reporting.Value, true);
        if (settings.TryGetValue(GovernanceSettingKeys.TenantDelegation.LockTenantOspreyProvider, out var osprey))
            LockTenantOspreyProvider = SettingValueSerializer.Deserialize(osprey.Value, true);
        if (settings.TryGetValue(GovernanceSettingKeys.TenantDelegation.LockTenantCoopProvider, out var coop))
            LockTenantCoopProvider = SettingValueSerializer.Deserialize(coop.Value, true);
        if (settings.TryGetValue(GovernanceSettingKeys.TenantDelegation.LockMcp, out var mcp))
            LockMcp = SettingValueSerializer.Deserialize(mcp.Value, true);
        if (settings.TryGetValue(GovernanceSettingKeys.TenantDelegation.LockMcpLegacySse, out var legacySse))
            LockMcpLegacySse = SettingValueSerializer.Deserialize(legacySse.Value, true);
    }
}
