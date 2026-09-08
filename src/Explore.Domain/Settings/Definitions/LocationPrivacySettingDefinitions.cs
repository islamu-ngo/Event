namespace Explore.Domain.Settings.Definitions;

using Explore.Domain.Constants;

public static class LocationPrivacySettingDefinitions
{
    public static readonly SettingDefinition AllowHomeLocations = Boolean(
        GovernanceSettingKeys.LocationPrivacy.AllowHomeLocations,
        "Allow governed Private Home locations");

    public static readonly SettingDefinition AllowPublicExactAddress = Boolean(
        GovernanceSettingKeys.LocationPrivacy.AllowPublicExactAddress,
        "Allow exact addresses on public location projections");

    public static readonly SettingDefinition AllowPublicCoordinates = Boolean(
        GovernanceSettingKeys.LocationPrivacy.AllowPublicCoordinates,
        "Allow exact coordinates on public location projections");

    public static readonly SettingDefinition MinimumHomeAudience = new(
        Key: GovernanceSettingKeys.LocationPrivacy.MinimumHomeAudience,
        ValueType: SettingValueType.String,
        DefaultValue: "\"NEVER\"",
        Category: "LocationPrivacy",
        Description: "Minimum audience required for Private Home details",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Tenant,
        IsLockable: true,
        AllowedValues: ["NEVER", "CONFIRMED_PARTICIPANT", "ANY_CURRENT_REGISTRANT"]);

    public static readonly SettingDefinition DefaultRevealOffset = new(
        Key: GovernanceSettingKeys.LocationPrivacy.DefaultRevealOffset,
        ValueType: SettingValueType.String,
        DefaultValue: "\"P30D\"",
        Category: "LocationPrivacy",
        Description: "Default delay before eligible location details may be revealed",
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Tenant,
        IsLockable: true);

    public static IReadOnlyList<SettingDefinition> All =>
    [
        AllowHomeLocations,
        AllowPublicExactAddress,
        AllowPublicCoordinates,
        MinimumHomeAudience,
        DefaultRevealOffset
    ];

    private static SettingDefinition Boolean(string key, string description) => new(
        Key: key,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "LocationPrivacy",
        Description: description,
        MinScope: SettingScope.Instance,
        MaxScope: SettingScope.Tenant,
        IsLockable: true);
}
