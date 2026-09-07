namespace Explore.Domain.Settings.Definitions;

using Explore.Domain.Constants;

public static class EventReportingIntakeSettingDefinitions
{
    public static readonly SettingDefinition IntakeEnabled = new(
        Key: GovernanceSettingKeys.EventReporting.IntakeEnabled,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "EventReporting",
        Description: "Whether the tenant accepts event reports",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Tenant,
        IsLockable: true,
        IsSensitive: false)
    {
        RequiresCoordinatedMutation = true,
    };

    public static IReadOnlyList<SettingDefinition> All => [IntakeEnabled];
}
