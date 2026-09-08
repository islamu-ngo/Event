namespace Explore.Domain.Settings.Definitions;

using Explore.Domain.Constants;

public static class UiShellSettingDefinitions
{
    public static readonly SettingDefinition RailPublicVisibility = Governance(
        GovernanceSettingKeys.UiShell.RailPublicVisibility,
        SettingValueType.String,
        "\"AuthenticatedOnly\"",
        "Controls whether the workspace rail appears for anonymous visitors",
        ["AuthenticatedOnly", "Always"]);

    public static readonly SettingDefinition DefaultNavModeEvents = NavigationMode(
        GovernanceSettingKeys.UiShell.DefaultNavModeEvents,
        "Default secondary-navigation mode for the Events workspace");

    public static readonly SettingDefinition DefaultNavModeStudio = NavigationMode(
        GovernanceSettingKeys.UiShell.DefaultNavModeStudio,
        "Default secondary-navigation mode for the Studio workspace");

    public static readonly SettingDefinition DefaultNavModeAi = NavigationMode(
        GovernanceSettingKeys.UiShell.DefaultNavModeAi,
        "Default secondary-navigation mode for the AI workspace");

    public static readonly SettingDefinition AllowUserNavOverride = Governance(
        GovernanceSettingKeys.UiShell.AllowUserNavOverride,
        SettingValueType.Boolean,
        "true",
        "Allows users to override the tenant's workspace navigation modes");

    public static readonly SettingDefinition OrganizerDefaultWorkspace = Governance(
        GovernanceSettingKeys.UiShell.OrganizerDefaultWorkspace,
        SettingValueType.String,
        "\"Events\"",
        "Default workspace for authenticated organizers",
        ["Events", "Studio"]);

    public static readonly SettingDefinition Layout = Preference(
        GovernanceSettingKeys.UiShellPreferences.Layout,
        SettingValueType.Json,
        "null",
        "Versioned workspace-shell layout snapshot for the current user");

    public static readonly SettingDefinition LastWorkspace = Preference(
        GovernanceSettingKeys.UiShellPreferences.LastWorkspace,
        SettingValueType.String,
        "\"\"",
        "Last valid workspace selected by the current user");

    public static readonly SettingDefinition LastActor = Preference(
        GovernanceSettingKeys.UiShellPreferences.LastActor,
        SettingValueType.String,
        "\"\"",
        "Last managed actor selected by the current user");

    public static readonly SettingDefinition LastSettingsScope = Preference(
        GovernanceSettingKeys.UiShellPreferences.LastSettingsScope,
        SettingValueType.String,
        "\"\"",
        "Last authorized administrative Settings scope selected by the current user");

    public static IReadOnlyList<SettingDefinition> All =>
    [
        RailPublicVisibility,
        DefaultNavModeEvents,
        DefaultNavModeStudio,
        DefaultNavModeAi,
        AllowUserNavOverride,
        OrganizerDefaultWorkspace,
        Layout,
        LastWorkspace,
        LastActor,
        LastSettingsScope
    ];

    private static SettingDefinition NavigationMode(string key, string description) =>
        Governance(key, SettingValueType.String, "\"Docked\"", description, ["Docked", "Collapsed"]);

    private static SettingDefinition Governance(
        string key,
        SettingValueType valueType,
        string defaultValue,
        string description,
        string[]? allowedValues = null) =>
        new(
            Key: key,
            ValueType: valueType,
            DefaultValue: defaultValue,
            Category: "UiShell",
            Description: description,
            MinScope: SettingScope.Instance,
            MaxScope: SettingScope.Tenant,
            IsLockable: true,
            AllowedValues: allowedValues);

    private static SettingDefinition Preference(
        string key,
        SettingValueType valueType,
        string defaultValue,
        string description) =>
        new(
            Key: key,
            ValueType: valueType,
            DefaultValue: defaultValue,
            Category: "UiShellPreferences",
            Description: description,
            MinScope: SettingScope.User,
            MaxScope: SettingScope.User,
            IsLockable: false);
}
