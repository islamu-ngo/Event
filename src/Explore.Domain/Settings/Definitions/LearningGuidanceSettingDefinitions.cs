namespace Explore.Domain.Settings.Definitions;

using Explore.Domain.Constants;

public static class LearningGuidanceSettingDefinitions
{
    public static readonly SettingDefinition AllowTenantEdits = new(
        Key: GovernanceSettingKeys.LearningGuidance.AllowTenantEdits,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "LearningGuidance",
        Description: "Whether tenant administrators can edit their tenant learning guidance",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Instance,
        IsLockable: false);

    public static IReadOnlyList<SettingDefinition> All =>
        [AllowTenantEdits];
}
