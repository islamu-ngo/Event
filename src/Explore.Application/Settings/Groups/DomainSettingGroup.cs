namespace Explore.Application.Settings.Groups;

using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Constants;

public class DomainSettingGroup : ISettingGroup
{
    public string InstanceBaseDomain { get; private set; } = string.Empty;
    public string AdminHost { get; private set; } = string.Empty;
    public bool AllowTenantCustomDomain { get; private set; } = true;
    public string TenantSubdomain { get; private set; } = string.Empty;
    public string TenantCustomDomain { get; private set; } = string.Empty;

    public static IEnumerable<string> SettingKeys =>
    [
        GovernanceSettingKeys.Domains.InstanceBaseDomain,
        GovernanceSettingKeys.Domains.AdminHost,
        GovernanceSettingKeys.Domains.AllowTenantCustomDomain,
        GovernanceSettingKeys.Domains.TenantSubdomain,
        GovernanceSettingKeys.Domains.TenantCustomDomain
    ];

    public void Populate(IReadOnlyDictionary<string, ResolvedSetting> settings)
    {
        if (settings.TryGetValue(GovernanceSettingKeys.Domains.InstanceBaseDomain, out var baseDomain))
            InstanceBaseDomain = SettingValueSerializer.DeserializeString(baseDomain.Value);
        if (settings.TryGetValue(GovernanceSettingKeys.Domains.AdminHost, out var adminHost))
            AdminHost = SettingValueSerializer.DeserializeString(adminHost.Value);
        if (settings.TryGetValue(GovernanceSettingKeys.Domains.AllowTenantCustomDomain, out var allowCustom))
            AllowTenantCustomDomain = SettingValueSerializer.Deserialize(allowCustom.Value, true);
        if (settings.TryGetValue(GovernanceSettingKeys.Domains.TenantSubdomain, out var subdomain))
            TenantSubdomain = SettingValueSerializer.DeserializeString(subdomain.Value);
        if (settings.TryGetValue(GovernanceSettingKeys.Domains.TenantCustomDomain, out var customDomain))
            TenantCustomDomain = SettingValueSerializer.DeserializeString(customDomain.Value);
    }
}
