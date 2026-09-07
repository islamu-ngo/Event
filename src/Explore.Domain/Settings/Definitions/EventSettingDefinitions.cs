namespace Explore.Domain.Settings.Definitions;

public static class EventSettingDefinitions
{
    public static readonly SettingDefinition MaxSessionsPerEvent = new(
        Key: "events.max_sessions_per_event",
        ValueType: SettingValueType.Integer,
        DefaultValue: "100",
        Category: "Events",
        Description: "Maximum number of sessions allowed per event",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition UserSubmissionEnabled = new(
        Key: "events.user_submission_enabled",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "Events",
        Description: "Whether tenant users are allowed to submit events",
        MaxScope: SettingScope.Tenant)
    {
        RequiresCoordinatedMutation = true,
    };

    public static readonly SettingDefinition RequireApproval = new(
        Key: "events.require_approval",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Events",
        Description: "Whether events require admin approval before publishing",
        MaxScope: SettingScope.Tenant)
    {
        RequiresCoordinatedMutation = true,
    };

    public static readonly SettingDefinition OrganizationSubmissionEnabled = new(
        Key: "events.organization_submission_enabled",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "Events",
        Description: "Whether organizations are allowed to submit events",
        MaxScope: SettingScope.Tenant)
    {
        RequiresCoordinatedMutation = true,
    };

    public static readonly SettingDefinition GroupSubmissionEnabled = new(
        Key: "events.group_submission_enabled",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "Events",
        Description: "Whether groups are allowed to submit events",
        MaxScope: SettingScope.Tenant)
    {
        RequiresCoordinatedMutation = true,
    };

    public static readonly SettingDefinition CardClickOpensDetailPage = new(
        Key: "events.card_click_opens_detail_page",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "Events",
        Description: "Whether clicking an event card navigates to the detail page",
        MaxScope: SettingScope.User);

    public static IReadOnlyList<SettingDefinition> All =>
        [MaxSessionsPerEvent, UserSubmissionEnabled, OrganizationSubmissionEnabled, GroupSubmissionEnabled, RequireApproval, CardClickOpensDetailPage];
}
