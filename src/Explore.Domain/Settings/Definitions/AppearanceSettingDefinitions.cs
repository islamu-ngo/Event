namespace Explore.Domain.Settings.Definitions;

public static class AppearanceSettingDefinitions
{
    public static readonly SettingDefinition ActiveProfileId = new(
        Key: "appearance.active_profile_id",
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Appearance",
        Description: "Effective active appearance profile reference for the current user scope",
        MaxScope: SettingScope.User);

    public static readonly SettingDefinition ThemeMode = new(
        Key: "appearance.theme_mode",
        ValueType: SettingValueType.String,
        DefaultValue: "\"system\"",
        Category: "Appearance",
        Description: "Effective theme mode selection. Allowed values: system, light, dark",
        MaxScope: SettingScope.User,
        AllowedValues: ["system", "light", "dark"]);

    public static readonly SettingDefinition Direction = new(
        Key: "appearance.direction",
        ValueType: SettingValueType.String,
        DefaultValue: "\"auto\"",
        Category: "Appearance",
        Description: "Text direction preference. auto = language-based, ltr = force left-to-right, rtl = force right-to-left",
        MaxScope: SettingScope.User,
        AllowedValues: ["auto", "ltr", "rtl"]);

    public static readonly SettingDefinition Language = new(
        Key: "appearance.language",
        ValueType: SettingValueType.String,
        DefaultValue: "\"en\"",
        Category: "Appearance",
        Description: "User language preference (ISO 639-1). Must be in the compile-time CultureRegistry.",
        MaxScope: SettingScope.User,
        AllowedValues: ["en", "fr", "ar"]);

    public static readonly SettingDefinition DefaultPresetId = new(
        Key: "appearance.default_preset_id",
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Appearance",
        Description: "Default theme preset for tenant or instance scope. Points to a UiThemePreset, not a user profile.",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition DefaultThemeMode = new(
        Key: "appearance.default_theme_mode",
        ValueType: SettingValueType.String,
        DefaultValue: "\"system\"",
        Category: "Appearance",
        Description: "Default theme mode for tenant or instance scope. Allowed values: system, light, dark",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Tenant,
        AllowedValues: ["system", "light", "dark"]);

    // Keep legacy key for backward compatibility during migration.
    // This will be removed once all clients are updated.
    public static readonly SettingDefinition LegacyDefaultThemeId = new(
        Key: "appearance.default_theme_id",
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Appearance",
        Description: "Legacy key — superseded by appearance.active_profile_id (user) and appearance.default_preset_id (tenant/instance).",
        MaxScope: SettingScope.User);

    public static IReadOnlyList<SettingDefinition> All =>
    [ActiveProfileId, ThemeMode, Direction, Language, DefaultPresetId, DefaultThemeMode, LegacyDefaultThemeId];
}
