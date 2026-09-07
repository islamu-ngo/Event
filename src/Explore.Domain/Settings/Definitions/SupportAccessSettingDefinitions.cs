using Explore.Domain.Constants;

namespace Explore.Domain.Settings.Definitions;

public static class SupportAccessSettingDefinitions
{
    public static readonly SettingDefinition Enabled = new(
        Key: GovernanceSettingKeys.SupportAccess.Enabled,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "SupportAccess",
        Description: "Global kill switch for admin support-access sessions",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance);

    public static readonly SettingDefinition MaxReadOnlyMinutes = new(
        Key: GovernanceSettingKeys.SupportAccess.MaxReadOnlyMinutes,
        ValueType: SettingValueType.Integer,
        DefaultValue: "30",
        Category: "SupportAccess",
        Description: "Maximum duration in minutes for read-only support-access sessions",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance);

    public static readonly SettingDefinition MaxWriteMinutes = new(
        Key: GovernanceSettingKeys.SupportAccess.MaxWriteMinutes,
        ValueType: SettingValueType.Integer,
        DefaultValue: "10",
        Category: "SupportAccess",
        Description: "Maximum duration in minutes for write-capable support-access sessions",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance);

    public static readonly SettingDefinition AllowWriteMode = new(
        Key: GovernanceSettingKeys.SupportAccess.AllowWriteMode,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "SupportAccess",
        Description: "Allow operators to start write-capable support-access sessions",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance);

    public static readonly SettingDefinition RequireTicketReference = new(
        Key: GovernanceSettingKeys.SupportAccess.RequireTicketReference,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "SupportAccess",
        Description: "Require a ticket or external reference before starting support access",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance);

    public static readonly SettingDefinition OneActiveSessionPerActor = new(
        Key: GovernanceSettingKeys.SupportAccess.OneActiveSessionPerActor,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "SupportAccess",
        Description: "Restrict each actor to one active support-access session",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance);

    public static IReadOnlyList<SettingDefinition> All =>
    [
        Enabled,
        MaxReadOnlyMinutes,
        MaxWriteMinutes,
        AllowWriteMode,
        RequireTicketReference,
        OneActiveSessionPerActor
    ];
}
