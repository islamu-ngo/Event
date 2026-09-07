namespace Explore.Application.Settings.Groups;

using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Constants;

public class AdminPortalSettingGroup : ISettingGroup
{
    public bool Enabled { get; private set; } = true;
    public string PublicUrl { get; private set; } = string.Empty;
    public bool AllowTenantAdminAccess { get; private set; }

    public static IEnumerable<string> SettingKeys =>
    [
        GovernanceSettingKeys.AdminPortal.Enabled,
        GovernanceSettingKeys.AdminPortal.PublicUrl,
        GovernanceSettingKeys.AdminPortal.AllowTenantAdminAccess
    ];

    public void Populate(IReadOnlyDictionary<string, ResolvedSetting> settings)
    {
        if (settings.TryGetValue(GovernanceSettingKeys.AdminPortal.Enabled, out var enabled))
            Enabled = SettingValueSerializer.Deserialize(enabled.Value, true);

        if (settings.TryGetValue(GovernanceSettingKeys.AdminPortal.PublicUrl, out var publicUrl))
            PublicUrl = SettingValueSerializer.DeserializeString(publicUrl.Value, string.Empty);

        if (settings.TryGetValue(GovernanceSettingKeys.AdminPortal.AllowTenantAdminAccess, out var tenantAccess))
            AllowTenantAdminAccess = SettingValueSerializer.Deserialize(tenantAccess.Value, false);
    }
}
