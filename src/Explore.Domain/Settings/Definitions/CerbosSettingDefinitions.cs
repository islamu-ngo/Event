namespace Explore.Domain.Settings.Definitions;

public static class CerbosSettingDefinitions
{
    public static readonly SettingDefinition TenantCustomizationEnabled = new(
        Key: "cerbos.tenant_customization_enabled",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Cerbos",
        Description: "Whether tenant-specific Cerbos policy customization is enabled");

    public static readonly SettingDefinition Mode = new(
        Key: "cerbos.mode",
        ValueType: SettingValueType.String,
        DefaultValue: "\"shared\"",
        Category: "Cerbos",
        Description: "Cerbos operational mode (shared, dedicated)",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition CustomEndpoint = new(
        Key: "cerbos.custom_endpoint",
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Cerbos",
        Description: "Custom Cerbos gRPC endpoint URL",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition CustomAdminEndpoint = new(
        Key: "cerbos.custom_admin_endpoint",
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Cerbos",
        Description: "Custom Cerbos Admin API endpoint URL",
        MaxScope: SettingScope.Tenant);

    public static IReadOnlyList<SettingDefinition> All =>
    [
        TenantCustomizationEnabled, Mode, CustomEndpoint,
        CustomAdminEndpoint
    ];
}
