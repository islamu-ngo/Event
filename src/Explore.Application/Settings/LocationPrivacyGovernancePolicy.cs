namespace Explore.Application.Settings;

using System.Text.Json;
using System.Xml;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Definitions;

internal static class LocationPrivacyGovernancePolicy
{
    private static readonly TimeSpan MaximumRevealOffset = TimeSpan.FromDays(30);

    internal static bool Handles(string key) => key is
        GovernanceSettingKeys.LocationPrivacy.AllowHomeLocations
        or GovernanceSettingKeys.LocationPrivacy.AllowPublicExactAddress
        or GovernanceSettingKeys.LocationPrivacy.AllowPublicCoordinates
        or GovernanceSettingKeys.LocationPrivacy.MinimumHomeAudience
        or GovernanceSettingKeys.LocationPrivacy.DefaultRevealOffset;

    internal static string DefaultStoredValue(string key) => key switch
    {
        GovernanceSettingKeys.LocationPrivacy.AllowHomeLocations =>
            LocationPrivacySettingDefinitions.AllowHomeLocations.DefaultValue,
        GovernanceSettingKeys.LocationPrivacy.AllowPublicExactAddress =>
            LocationPrivacySettingDefinitions.AllowPublicExactAddress.DefaultValue,
        GovernanceSettingKeys.LocationPrivacy.AllowPublicCoordinates =>
            LocationPrivacySettingDefinitions.AllowPublicCoordinates.DefaultValue,
        GovernanceSettingKeys.LocationPrivacy.MinimumHomeAudience =>
            LocationPrivacySettingDefinitions.MinimumHomeAudience.DefaultValue,
        GovernanceSettingKeys.LocationPrivacy.DefaultRevealOffset =>
            LocationPrivacySettingDefinitions.DefaultRevealOffset.DefaultValue,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown location-privacy setting key.")
    };

    internal static bool TryParse(
        string key,
        string storedValue,
        out LocationPrivacyGovernanceSettingValue value,
        out string? error)
    {
        value = default;
        error = null;
        if (!Handles(key))
        {
            error = $"Setting '{key}' is not a location-privacy governance key.";
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(storedValue);
            JsonElement element = document.RootElement;
            switch (key)
            {
                case GovernanceSettingKeys.LocationPrivacy.AllowHomeLocations:
                case GovernanceSettingKeys.LocationPrivacy.AllowPublicExactAddress:
                case GovernanceSettingKeys.LocationPrivacy.AllowPublicCoordinates:
                    if (element.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                    {
                        error = $"Setting '{key}' requires a JSON boolean.";
                        return false;
                    }

                    value = LocationPrivacyGovernanceSettingValue.FromBoolean(element.GetBoolean());
                    return true;

                case GovernanceSettingKeys.LocationPrivacy.MinimumHomeAudience:
                    if (element.ValueKind != JsonValueKind.String
                        || !TryParseAudience(element.GetString(), out LocationDisclosureAudienceEnum audience))
                    {
                        error = $"Setting '{key}' requires NEVER, CONFIRMED_PARTICIPANT, or ANY_CURRENT_REGISTRANT.";
                        return false;
                    }

                    value = LocationPrivacyGovernanceSettingValue.FromAudience(audience);
                    return true;

                case GovernanceSettingKeys.LocationPrivacy.DefaultRevealOffset:
                    if (element.ValueKind != JsonValueKind.String
                        || !TryParseDuration(element.GetString(), out TimeSpan duration))
                    {
                        error = $"Setting '{key}' requires an ISO-8601 duration from PT0S through P30D.";
                        return false;
                    }

                    value = LocationPrivacyGovernanceSettingValue.FromDuration(duration);
                    return true;
            }
        }
        catch (JsonException)
        {
            error = $"Setting '{key}' contains malformed JSON.";
            return false;
        }

        error = $"Setting '{key}' could not be parsed.";
        return false;
    }

    internal static bool IsTenantWidening(
        LocationPrivacyGovernanceSettingValue instanceValue,
        LocationPrivacyGovernanceSettingValue tenantValue)
        => tenantValue.RestrictionRank < instanceValue.RestrictionRank;

    internal static LocationPrivacyGovernanceSettingValue MostRestrictive(
        LocationPrivacyGovernanceSettingValue first,
        LocationPrivacyGovernanceSettingValue second)
        => first.RestrictionRank >= second.RestrictionRank ? first : second;

    internal static bool IsTightening(
        LocationPrivacyGovernanceSettingValue previous,
        LocationPrivacyGovernanceSettingValue current)
        => current.RestrictionRank > previous.RestrictionRank;

    private static bool TryParseAudience(
        string? value,
        out LocationDisclosureAudienceEnum audience)
    {
        audience = value switch
        {
            "NEVER" => LocationDisclosureAudienceEnum.Never,
            "CONFIRMED_PARTICIPANT" => LocationDisclosureAudienceEnum.ConfirmedParticipant,
            "ANY_CURRENT_REGISTRANT" => LocationDisclosureAudienceEnum.AnyCurrentRegistrant,
            _ => 0
        };
        return Enum.IsDefined(audience);
    }

    private static bool TryParseDuration(string? text, out TimeSpan value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text)
            || text[0] != 'P'
            || ContainsCalendarUnit(text))
        {
            return false;
        }

        try
        {
            value = XmlConvert.ToTimeSpan(text);
            return value >= TimeSpan.Zero && value <= MaximumRevealOffset;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            return false;
        }
    }

    private static bool ContainsCalendarUnit(string text)
    {
        int timeSeparator = text.IndexOf('T');
        ReadOnlySpan<char> datePart = timeSeparator < 0
            ? text.AsSpan(1)
            : text.AsSpan(1, timeSeparator - 1);
        return datePart.Contains('Y') || datePart.Contains('M');
    }
}

internal readonly record struct LocationPrivacyGovernanceSettingValue(
    bool? Boolean,
    LocationDisclosureAudienceEnum? Audience,
    TimeSpan? Duration,
    long RestrictionRank)
{
    internal static LocationPrivacyGovernanceSettingValue FromBoolean(bool value) =>
        new(value, null, null, value ? 0 : 1);

    internal static LocationPrivacyGovernanceSettingValue FromAudience(
        LocationDisclosureAudienceEnum value) =>
        new(null, value, null, value switch
        {
            LocationDisclosureAudienceEnum.AnyCurrentRegistrant => 0,
            LocationDisclosureAudienceEnum.ConfirmedParticipant => 1,
            LocationDisclosureAudienceEnum.Never => 2,
            _ => long.MaxValue
        });

    internal static LocationPrivacyGovernanceSettingValue FromDuration(TimeSpan value) =>
        new(null, null, value, value.Ticks);
}
