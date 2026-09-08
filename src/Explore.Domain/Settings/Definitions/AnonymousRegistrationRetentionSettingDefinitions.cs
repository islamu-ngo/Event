using System.Globalization;
using Explore.Domain.Constants;
using Explore.Domain.Services.Registration;

namespace Explore.Domain.Settings.Definitions;

public static class AnonymousRegistrationRetentionSettingDefinitions
{
    public static readonly SettingDefinition RetentionDays = new(
        Key: GovernanceSettingKeys.AnonymousRegistration.RetentionDays,
        ValueType: SettingValueType.String,
        DefaultValue: "\"7\"",
        Category: "AnonymousRegistration",
        Description: "Days after the event end that anonymous registration names and answers remain available",
        AllowedValues: Enumerable.Range(AnonymousRegistrationRetentionPolicy.MinimumDays,
            AnonymousRegistrationRetentionPolicy.MaximumDays + 1).Select(value => value.ToString(CultureInfo.InvariantCulture)));

    public static IReadOnlyList<SettingDefinition> All => [RetentionDays];
}
