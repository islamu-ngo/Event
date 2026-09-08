namespace Explore.Domain.Settings.Definitions;

public static class ModuleSettingDefinitions
{
    public static readonly SettingDefinition IslamicEnabled = new(
        Key: "modules.islamic_enabled",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "Modules",
        Description: "Enable Islamic event module",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition TechEnabled = new(
        Key: "modules.tech_enabled",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "Modules",
        Description: "Enable Tech event module",
        MaxScope: SettingScope.Tenant);

    public static IReadOnlyList<SettingDefinition> All =>
        [IslamicEnabled, TechEnabled];
}
