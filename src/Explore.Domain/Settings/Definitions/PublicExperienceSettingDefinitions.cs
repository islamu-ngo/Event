namespace Explore.Domain.Settings.Definitions;

using Explore.Domain.Constants;

public static class PublicExperienceSettingDefinitions
{
    public static readonly SettingDefinition VisitorAccessMode = PublicExperienceDefinition(
        key: GovernanceSettingKeys.PublicExperience.VisitorAccessMode,
        valueType: SettingValueType.String,
        defaultValue: "\"FullRegistrationAndAuth\"",
        description: "Public visitor access; full mode exposes only actually available account-provider capabilities",
        allowedValues: ["FullRegistrationAndAuth", "AnonymousOnly", "DirectoryListingOnly"]);

    public static readonly SettingDefinition Mode = PublicExperienceDefinition(
        key: GovernanceSettingKeys.PublicExperience.Mode,
        valueType: SettingValueType.String,
        defaultValue: "\"DiscoveryCentric\"",
        description: "Anonymous public experience posture. Allowed values: DiscoveryCentric, OrganizationCentric",
        allowedValues: ["DiscoveryCentric", "OrganizationCentric"]);

    public static readonly SettingDefinition EventCatalogLabel = PublicExperienceDefinition(
        key: GovernanceSettingKeys.PublicExperience.EventCatalogLabel,
        valueType: SettingValueType.String,
        defaultValue: "\"Events\"",
        description: "Display label for the public event catalog entry point");

    public static readonly SettingDefinition PrimaryOrganizationId = PublicExperienceDefinition(
        key: GovernanceSettingKeys.PublicExperience.PrimaryOrganizationId,
        valueType: SettingValueType.String,
        defaultValue: "\"\"",
        description: "Optional tenant-local primary organization identifier for organization-centric public experience metadata");

    public static readonly SettingDefinition HomeBlocks = PublicExperienceDefinition(
        key: GovernanceSettingKeys.PublicExperience.HomeBlocks,
        valueType: SettingValueType.Json,
        defaultValue: "{\"schemaVersion\":1,\"blocks\":[]}",
        description: "Versioned public home block configuration document");

    public static readonly SettingDefinition Ctas = PublicExperienceDefinition(
        key: GovernanceSettingKeys.PublicExperience.Ctas,
        valueType: SettingValueType.Json,
        defaultValue: "{\"schemaVersion\":1,\"ctas\":[]}",
        description: "Versioned public call-to-action configuration document");

    public static readonly SettingDefinition EventSectionPresets = PublicExperienceDefinition(
        key: GovernanceSettingKeys.PublicExperience.EventSectionPresets,
        valueType: SettingValueType.Json,
        defaultValue: "{\"schemaVersion\":1,\"presets\":[]}",
        description: "Versioned public event-section preset configuration document");

    public static readonly SettingDefinition DiscoveryAreas = PublicExperienceDefinition(
        key: GovernanceSettingKeys.PublicExperience.DiscoveryAreas,
        valueType: SettingValueType.Json,
        defaultValue: "{\"schemaVersion\":1,\"areas\":[]}",
        description: "Versioned public discovery-area configuration with stable IDs and coarse centroids");

    public static readonly SettingDefinition AnnouncementBarEnabled = PublicExperienceDefinition(
        key: GovernanceSettingKeys.PublicExperience.AnnouncementBarEnabled,
        valueType: SettingValueType.Boolean,
        defaultValue: "false",
        description: "Controls whether the tenant announcement bar is displayed above public navigation");

    public static readonly SettingDefinition AnnouncementBarMessage = PublicExperienceDefinition(
        key: GovernanceSettingKeys.PublicExperience.AnnouncementBarMessage,
        valueType: SettingValueType.String,
        defaultValue: "\"\"",
        description: "Tenant announcement bar message text");

    public static readonly SettingDefinition AnnouncementBarLinkText = PublicExperienceDefinition(
        key: GovernanceSettingKeys.PublicExperience.AnnouncementBarLinkText,
        valueType: SettingValueType.String,
        defaultValue: "\"\"",
        description: "Optional tenant announcement bar link label");

    public static readonly SettingDefinition AnnouncementBarLinkUrl = PublicExperienceDefinition(
        key: GovernanceSettingKeys.PublicExperience.AnnouncementBarLinkUrl,
        valueType: SettingValueType.String,
        defaultValue: "\"\"",
        description: "Optional tenant announcement bar link URL");

    public static readonly SettingDefinition AnnouncementBarRevision = PublicExperienceDefinition(
        key: GovernanceSettingKeys.PublicExperience.AnnouncementBarRevision,
        valueType: SettingValueType.Integer,
        defaultValue: "0",
        description: "Tenant announcement bar revision used to reset user dismissals");

    public static readonly SettingDefinition AnnouncementBarDismissedRevision = new(
        Key: GovernanceSettingKeys.PublicExperiencePreferences.AnnouncementBarDismissedRevision,
        ValueType: SettingValueType.Integer,
        DefaultValue: "-1",
        Category: "PublicExperiencePreferences",
        Description: "Latest announcement bar revision dismissed by the current user",
        MinScope: SettingScope.User,
        MaxScope: SettingScope.User,
        IsLockable: false);

    public static readonly SettingDefinition HomeDiscoveryAreaId = new(
        Key: GovernanceSettingKeys.HomeDiscoveryPreferences.AreaId,
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "HomeDiscoveryPreferences",
        Description: "Stable public discovery-area identifier selected by the current user",
        MinScope: SettingScope.User,
        MaxScope: SettingScope.User,
        IsLockable: false);

    public static readonly SettingDefinition HomeDiscoveryMode = new(
        Key: GovernanceSettingKeys.HomeDiscoveryPreferences.Mode,
        ValueType: SettingValueType.String,
        DefaultValue: "\"area\"",
        Category: "HomeDiscoveryPreferences",
        Description: "Current user's public home discovery mode",
        MinScope: SettingScope.User,
        MaxScope: SettingScope.User,
        AllowedValues: ["area", "online", "all"],
        IsLockable: false);

    public static IReadOnlyList<SettingDefinition> All =>
    [
        Mode,
        VisitorAccessMode,
        EventCatalogLabel,
        PrimaryOrganizationId,
        HomeBlocks,
        Ctas,
        EventSectionPresets,
        DiscoveryAreas,
        AnnouncementBarEnabled,
        AnnouncementBarMessage,
        AnnouncementBarLinkText,
        AnnouncementBarLinkUrl,
        AnnouncementBarRevision,
        AnnouncementBarDismissedRevision,
        HomeDiscoveryAreaId,
        HomeDiscoveryMode
    ];

    private static SettingDefinition PublicExperienceDefinition(
        string key,
        SettingValueType valueType,
        string defaultValue,
        string description,
        string[]? allowedValues = null) =>
        new(
            Key: key,
            ValueType: valueType,
            DefaultValue: defaultValue,
            Category: "PublicExperience",
            Description: description,
            MinScope: SettingScope.Instance,
            MaxScope: SettingScope.Tenant,
            AllowedValues: allowedValues);
}
