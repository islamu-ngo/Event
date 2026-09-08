namespace Explore.Domain.Settings.Definitions;

public static class BrandingSettingDefinitions
{
    public static readonly SettingDefinition DisplayName = new(
        Key: "branding.display_name",
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Branding",
        Description: "Default brand display name shown when tenants do not override branding",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition SupportEmail = new(
        Key: "branding.support_email",
        ValueType: SettingValueType.String,
        DefaultValue: "null",
        Category: "Branding",
        Description: "Public support contact for the instance site, independent of outbound email delivery",
        MaxScope: SettingScope.Instance);

    public static readonly SettingDefinition LogoUrl = new(
        Key: "branding.logo_url",
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Branding",
        Description: "Default logo URL shown when tenants do not override branding",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition FaviconUrl = new(
        Key: "branding.favicon_url",
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Branding",
        Description: "Default favicon URL shown when tenants do not override branding",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition CustomCssUrl = new(
        Key: "branding.custom_css_url",
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Branding",
        Description: "Default custom CSS URL applied when tenants do not override branding",
        MaxScope: SettingScope.Tenant);

    public static IReadOnlyList<SettingDefinition> All =>
        [DisplayName, SupportEmail, LogoUrl, FaviconUrl, CustomCssUrl];
}
