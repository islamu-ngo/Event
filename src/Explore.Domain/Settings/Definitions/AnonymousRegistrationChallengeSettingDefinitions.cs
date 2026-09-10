
using Explore.Domain.Constants;

namespace Explore.Domain.Settings.Definitions;

public static class AnonymousRegistrationChallengeSettingDefinitions
{
    public static readonly SettingDefinition TenantPermitsPerMinute = new(
        Key: GovernanceSettingKeys.AnonymousRegistrationChallenge.TenantPermitsPerMinute,
        ValueType: SettingValueType.String,
        DefaultValue: "\"600\"",
        Category: "AnonymousRegistrationChallenge",
        Description: "Maximum anonymous challenges issued per tenant per database UTC minute",
        AllowedValues: ["1", "10", "30", "60", "120", "300", "600", "1200", "3000", "10000"]);

    public static readonly SettingDefinition EventPermitsPerMinute = new(
        Key: GovernanceSettingKeys.AnonymousRegistrationChallenge.EventPermitsPerMinute,
        ValueType: SettingValueType.String,
        DefaultValue: "\"120\"",
        Category: "AnonymousRegistrationChallenge",
        Description: "Maximum anonymous challenges issued per event per database UTC minute",
        AllowedValues: ["1", "10", "30", "60", "120", "300", "600", "1200", "3000", "10000"]);

    public static readonly SettingDefinition Difficulty = new(
        Key: GovernanceSettingKeys.AnonymousRegistrationChallenge.Difficulty,
        ValueType: SettingValueType.String,
        DefaultValue: "\"18\"",
        Category: "AnonymousRegistrationChallenge",
        Description: "Required leading zero SHA-256 bits for anonymous registration proof",
        AllowedValues: ["16", "17", "18", "19", "20", "21", "22"]);

    public static IReadOnlyList<SettingDefinition> All => [TenantPermitsPerMinute, EventPermitsPerMinute, Difficulty];
}
