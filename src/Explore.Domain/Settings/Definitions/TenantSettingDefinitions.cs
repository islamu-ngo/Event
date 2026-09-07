namespace Explore.Domain.Settings.Definitions;

public static class TenantSettingDefinitions
{
    public static readonly SettingDefinition SelfServiceRegistration = new(
        Key: "tenants.self_service_registration",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Tenant",
        Description: "Whether tenants can self-register without manual instance admin invitation");

    public static readonly SettingDefinition WhiteLabelingEnabled = new(
        Key: "tenants.white_labeling_enabled",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Tenant",
        Description: "Whether tenant-level white-label branding overrides are enabled in multi-tenant mode");

    public static readonly SettingDefinition DefaultOrganizationId = new(
        Key: "tenants.default_organization_id",
        ValueType: SettingValueType.String,
        DefaultValue: "",
        Category: "Tenant",
        Description: "Default organization for a tenant (GUID or empty)",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition DefaultGroupId = new(
        Key: "tenants.default_group_id",
        ValueType: SettingValueType.String,
        DefaultValue: "",
        Category: "Tenant",
        Description: "Default group for a tenant (GUID or empty)",
        MaxScope: SettingScope.Tenant);

    public static IReadOnlyList<SettingDefinition> All =>
        [SelfServiceRegistration, WhiteLabelingEnabled, DefaultOrganizationId, DefaultGroupId];
}
