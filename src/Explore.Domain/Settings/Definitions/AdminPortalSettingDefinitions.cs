namespace Explore.Domain.Settings.Definitions;

using Explore.Domain.Constants;

public static class AdminPortalSettingDefinitions
{
    public static readonly SettingDefinition Enabled = new(
        Key: GovernanceSettingKeys.AdminPortal.Enabled,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "AdminPortal",
        Description: "Whether the dedicated Control Plane Admin Portal is enabled.",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance,
        IsLockable: false);

    public static readonly SettingDefinition PublicUrl = new(
        Key: GovernanceSettingKeys.AdminPortal.PublicUrl,
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "AdminPortal",
        Description: "Public URL used when the Admin Portal needs to generate absolute operator links.",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance,
        IsLockable: false);

    public static readonly SettingDefinition AllowTenantAdminAccess = new(
        Key: GovernanceSettingKeys.AdminPortal.AllowTenantAdminAccess,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "AdminPortal",
        Description: "Whether tenant administrators may access tenant-scoped Admin Portal areas.",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance,
        IsLockable: false);

    public static IReadOnlyList<SettingDefinition> All =>
    [
        Enabled,
        PublicUrl,
        AllowTenantAdminAccess
    ];
}
