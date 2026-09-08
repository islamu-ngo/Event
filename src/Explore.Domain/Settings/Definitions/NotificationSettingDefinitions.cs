namespace Explore.Domain.Settings.Definitions;

public static class NotificationSettingDefinitions
{
    public static readonly SettingDefinition DisplayDensity = new(
        Key: "notifications.display_density",
        ValueType: SettingValueType.String,
        DefaultValue: "comfortable",
        Category: "Notifications",
        Description: "Notification display density in panels and inbox",
        MaxScope: SettingScope.User,
        AllowedValues: ["comfortable", "compact"]);

    public static readonly SettingDefinition DefaultScope = new(
        Key: "notifications.default_scope",
        ValueType: SettingValueType.String,
        DefaultValue: "all",
        Category: "Notifications",
        Description: "Default scope filter when opening notifications",
        MaxScope: SettingScope.User,
        AllowedValues: ["all", "personal", "organization", "group"]);

    public static readonly SettingDefinition PollIntervalSeconds = new(
        Key: "notifications.poll_interval_seconds",
        ValueType: SettingValueType.Integer,
        DefaultValue: "60",
        Category: "Notifications",
        Description: "Polling interval in seconds for unread notification count",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition MaxBadgeCount = new(
        Key: "notifications.max_badge_count",
        ValueType: SettingValueType.Integer,
        DefaultValue: "99",
        Category: "Notifications",
        Description: "Maximum count displayed on the notification badge before showing N+",
        MaxScope: SettingScope.Tenant);

    public static IReadOnlyList<SettingDefinition> All =>
        [DisplayDensity, DefaultScope, PollIntervalSeconds, MaxBadgeCount];
}
