namespace Explore.Domain.Settings.Definitions;

public static class LocalizationSettingDefinitions
{
    public static readonly SettingDefinition DefaultLanguage = new(
        Key: "localization.default_language",
        ValueType: SettingValueType.String,
        DefaultValue: "\"en\"",
        Category: "Localization",
        Description: "Default instance language code (ISO 639-1). Must be in the compile-time CultureRegistry.",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition TmsProvider = new(
        Key: "localization.tms_provider",
        ValueType: SettingValueType.String,
        DefaultValue: "\"none\"",
        Category: "Localization",
        Description: "Active Translation Management System provider (none uses offline bundles).",
        MaxScope: SettingScope.Tenant,
        AllowedValues: ["none", "tolgee", "weblate"]);

    public static readonly SettingDefinition TmsApiUrl = new(
        Key: "localization.tms_api_url",
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Localization",
        Description: "TMS API base URL (e.g. https://app.tolgee.io).",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition TmsProjectId = new(
        Key: "localization.tms_project_id",
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Localization",
        Description: "TMS project identifier.",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition TmsComponent = new(
        Key: "localization.tms_component",
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Localization",
        Description: "Weblate component slug (Weblate-specific, leave empty for Tolgee).",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition EnabledLanguages = new(
        Key: "localization.enabled_languages",
        ValueType: SettingValueType.String,
        DefaultValue: "\"en,fr,ar\"",
        Category: "Localization",
        Description: "Comma-separated culture codes the instance has enabled (must be a subset of the compile-time CultureRegistry).",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition FallbackLanguage = new(
        Key: "localization.fallback_language",
        ValueType: SettingValueType.String,
        DefaultValue: "\"en\"",
        Category: "Localization",
        Description: "Fallback language used when a requested translation key is missing; must be in EnabledLanguages.",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition ClientPickerEnabled = new(
        Key: "localization.client_picker_enabled",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "Localization",
        Description: "Kill-switch: hides the in-app language picker when false, without a redeploy.",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition ForceOfflineMode = new(
        Key: "localization.force_offline_mode",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Localization",
        Description: "Emergency toggle: routes RuntimeTranslationProvider through OfflineTranslationProvider regardless of tms_provider.",
        MaxScope: SettingScope.Tenant);

    public static IReadOnlyList<SettingDefinition> All =>
    [
        DefaultLanguage,
        TmsProvider,
        TmsApiUrl,
        TmsProjectId,
        TmsComponent,
        EnabledLanguages,
        FallbackLanguage,
        ClientPickerEnabled,
        ForceOfflineMode
    ];
}
