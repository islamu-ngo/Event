namespace Explore.Domain.Settings.Definitions;

public static class GroupSettingDefinitions
{
    public static readonly SettingDefinition SelfRegistrationEnabled = new(
        Key: "groups.self_registration_enabled",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "Groups",
        Description: "Whether users can self-register groups",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition RequireApproval = new(
        Key: "groups.require_approval",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Groups",
        Description: "Whether new groups require admin approval before becoming active",
        MaxScope: SettingScope.Tenant);

    public static IReadOnlyList<SettingDefinition> All =>
        [SelfRegistrationEnabled, RequireApproval];
}
