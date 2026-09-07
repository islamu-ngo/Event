using Explore.Domain.Constants;

namespace Explore.Domain.Settings.Definitions;

public static class AddressGovernanceSettingDefinitions
{
    public const string Category = "AddressGovernance";

    public static readonly SettingDefinition CreationMode = new(
        Key: GovernanceSettingKeys.AddressGovernance.CreationMode,
        ValueType: SettingValueType.String,
        DefaultValue: "\"Disabled\"",
        Category: Category,
        Description: "Effective mode governing manual address creation",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Tenant,
        IsLockable: true,
        AllowedValues:
        [
            "Disabled",
            "AdminOnly",
            "OrganizationGoverned",
            "OpenWithModeration"
        ]);

    public static readonly SettingDefinition OrganizationCreationGrant = new(
        Key: GovernanceSettingKeys.AddressGovernance.OrganizationCreationGrant,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: Category,
        Description: "Allow authorized members of this organization to create scoped addresses",
        MinScope: SettingScope.Organization,
        MaxScope: SettingScope.Organization,
        IsLockable: false);

    public static IReadOnlyList<SettingDefinition> All =>
        [CreationMode, OrganizationCreationGrant];
}
