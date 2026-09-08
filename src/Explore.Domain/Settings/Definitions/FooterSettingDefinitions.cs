namespace Explore.Domain.Settings.Definitions;

public static class FooterSettingDefinitions
{
    public static readonly SettingDefinition Enabled = new(
        Key: "footer.enabled",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "Footer",
        Description: "Master switch to show or hide the site footer",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition Template = new(
        Key: "footer.template",
        ValueType: SettingValueType.String,
        DefaultValue: "\"standard-3-col\"",
        Category: "Footer",
        Description: "Layout template key: minimal | standard-2-col | standard-3-col | community",
        MaxScope: SettingScope.Tenant,
        AllowedValues: ["\"minimal\"", "\"standard-2-col\"", "\"standard-3-col\"", "\"community\""]);

    public static readonly SettingDefinition ShowDescription = new(
        Key: "footer.show_description",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "Footer",
        Description: "Whether to display the brand description block in the footer",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition DescriptionText = new(
        Key: "footer.description_text",
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Footer",
        Description: "Custom brand description shown in the footer brand column",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition ShowSocialLinks = new(
        Key: "footer.show_social_links",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "Footer",
        Description: "Whether to display the social media icons bar in the footer",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition SocialLinks = new(
        Key: "footer.social_links",
        ValueType: SettingValueType.Json,
        DefaultValue: "[]",
        Category: "Footer",
        Description: "JSON array of social link objects: [{\"Platform\":\"twitter\",\"Url\":\"https://…\",\"Label\":\"Twitter\"}]",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition CopyrightText = new(
        Key: "footer.copyright_text",
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Footer",
        Description: "Custom copyright text (empty = auto-generated from brand name and current year)",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition ShowCookieSettingsLink = new(
        Key: "footer.show_cookie_settings_link",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "Footer",
        Description: "Whether to display the Cookie Settings link in the footer legal section",
        MaxScope: SettingScope.Tenant);

    // ── Instance-level lock flags ─────────────────────────────────────────────

    public static readonly SettingDefinition LockTenantTemplate = new(
        Key: "footer.lock_tenant_template",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Footer",
        Description: "When true, tenants cannot change the footer template",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance,
        IsLockable: false);

    public static readonly SettingDefinition LockTenantLinkGroups = new(
        Key: "footer.lock_tenant_link_groups",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Footer",
        Description: "When true, tenants cannot create or edit footer link groups (instance defaults are shown instead)",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance,
        IsLockable: false);

    public static readonly SettingDefinition LockTenantSocialLinks = new(
        Key: "footer.lock_tenant_social_links",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Footer",
        Description: "When true, tenants cannot override social link URLs",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance,
        IsLockable: false);

    public static readonly SettingDefinition LockTenantDescription = new(
        Key: "footer.lock_tenant_description",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Footer",
        Description: "When true, tenants cannot override the footer description text",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance,
        IsLockable: false);

    public static readonly SettingDefinition LockTenantCopyright = new(
        Key: "footer.lock_tenant_copyright",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Footer",
        Description: "When true, tenants cannot override the footer copyright text",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance,
        IsLockable: false);

    public static IReadOnlyList<SettingDefinition> All =>
    [
        Enabled,
        Template,
        ShowDescription,
        DescriptionText,
        ShowSocialLinks,
        SocialLinks,
        CopyrightText,
        ShowCookieSettingsLink,
        LockTenantTemplate,
        LockTenantLinkGroups,
        LockTenantSocialLinks,
        LockTenantDescription,
        LockTenantCopyright,
    ];
}
