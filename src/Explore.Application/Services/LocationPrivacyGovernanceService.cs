using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;

namespace Explore.Application.Services;

public sealed class LocationPrivacyGovernanceService(
    ISystemSettingRepository systemSettings,
    ITenantSettingRepository tenantSettings) : ILocationPrivacyGovernanceService
{
    private static readonly TimeSpan MaximumRevealOffset = TimeSpan.FromDays(30);
    private static readonly string[] Keys =
    [
        GovernanceSettingKeys.LocationPrivacy.AllowHomeLocations,
        GovernanceSettingKeys.LocationPrivacy.AllowPublicExactAddress,
        GovernanceSettingKeys.LocationPrivacy.AllowPublicCoordinates,
        GovernanceSettingKeys.LocationPrivacy.MinimumHomeAudience,
        GovernanceSettingKeys.LocationPrivacy.DefaultRevealOffset
    ];

    public async Task<EffectiveLocationPrivacyGovernance> ResolveAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            return EffectiveLocationPrivacyGovernance.FailClosed(
                LocationPrivacyGovernanceReasonCode.InvalidTenantId);
        }

        try
        {
            List<SystemSetting> instanceRows = await systemSettings.GetAllSettings(
                "LocationPrivacy",
                cancellationToken);

            if (!TryIndex(instanceRows, x => x.SettingKey, x => x.Value, out var instance)
                || !TryResolve(instance, DefaultValues(), out var instanceValues))
            {
                return EffectiveLocationPrivacyGovernance.FailClosed(
                    LocationPrivacyGovernanceReasonCode.InvalidInstanceSetting);
            }

            List<TenantSetting> tenantRows = await tenantSettings.GetByTenantAndKeys(
                tenantId,
                Keys,
                cancellationToken);

            if (!TryIndex(tenantRows, x => x.SettingKey, x => x.Value, out var tenant)
                || !TryResolve(tenant, instanceValues, out var tenantValues))
            {
                return EffectiveLocationPrivacyGovernance.FailClosed(
                    LocationPrivacyGovernanceReasonCode.InvalidTenantSetting);
            }

            string instanceVersion = ComputeVersion(instance);
            string? tenantVersion = tenant.Count > 0 ? ComputeVersion(tenant) : null;
            return new(
                IsResolved: true,
                LocationPrivacyGovernanceReasonCode.Resolved,
                instanceValues.AllowHomeLocations && tenantValues.AllowHomeLocations,
                instanceValues.AllowPublicExactAddress && tenantValues.AllowPublicExactAddress,
                instanceValues.AllowPublicCoordinates && tenantValues.AllowPublicCoordinates,
                MoreRestrictive(instanceValues.MinimumHomeAudience, tenantValues.MinimumHomeAudience),
                instanceValues.DefaultRevealOffset >= tenantValues.DefaultRevealOffset
                    ? instanceValues.DefaultRevealOffset
                    : tenantValues.DefaultRevealOffset)
            {
                Metadata = new(
                    tenant.Count > 0
                        ? LocationPrivacyGovernanceSource.InstanceAndTenant
                        : instance.Count > 0
                            ? LocationPrivacyGovernanceSource.Instance
                            : LocationPrivacyGovernanceSource.ConservativeDefaults,
                    instanceVersion,
                    tenantVersion,
                    ComputeVersion(instanceVersion, tenantVersion ?? "inherited"))
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return EffectiveLocationPrivacyGovernance.FailClosed(
                LocationPrivacyGovernanceReasonCode.RepositoryUnavailable);
        }
    }

    private static GovernanceValues DefaultValues() => new(
        AllowHomeLocations: false,
        AllowPublicExactAddress: false,
        AllowPublicCoordinates: false,
        MinimumHomeAudience: LocationDisclosureAudienceEnum.Never,
        DefaultRevealOffset: MaximumRevealOffset);

    private static bool TryIndex<T>(
        IEnumerable<T> rows,
        Func<T, string> keySelector,
        Func<T, string> valueSelector,
        out IReadOnlyDictionary<string, string> values)
    {
        var indexed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (T row in rows)
        {
            string key = keySelector(row);
            if (!Keys.Contains(key, StringComparer.Ordinal))
            {
                values = null!;
                return false;
            }

            string value = valueSelector(row);
            if (indexed.TryGetValue(key, out string? existing)
                && !string.Equals(existing, value, StringComparison.Ordinal))
            {
                values = null!;
                return false;
            }

            indexed[key] = value;
        }

        values = indexed;
        return true;
    }

    private static bool TryResolve(
        IReadOnlyDictionary<string, string> rows,
        GovernanceValues inherited,
        out GovernanceValues values)
    {
        bool allowHomeLocations = inherited.AllowHomeLocations;
        bool allowPublicExactAddress = inherited.AllowPublicExactAddress;
        bool allowPublicCoordinates = inherited.AllowPublicCoordinates;
        LocationDisclosureAudienceEnum minimumHomeAudience = inherited.MinimumHomeAudience;
        TimeSpan defaultRevealOffset = inherited.DefaultRevealOffset;
        bool resolved = TryBoolean(rows, GovernanceSettingKeys.LocationPrivacy.AllowHomeLocations, inherited.AllowHomeLocations, out allowHomeLocations)
            && TryBoolean(rows, GovernanceSettingKeys.LocationPrivacy.AllowPublicExactAddress, inherited.AllowPublicExactAddress, out allowPublicExactAddress)
            && TryBoolean(rows, GovernanceSettingKeys.LocationPrivacy.AllowPublicCoordinates, inherited.AllowPublicCoordinates, out allowPublicCoordinates)
            && TryAudience(rows, inherited.MinimumHomeAudience, out minimumHomeAudience)
            && TryDuration(rows, inherited.DefaultRevealOffset, out defaultRevealOffset);
        values = resolved
            ? new(
                allowHomeLocations,
                allowPublicExactAddress,
                allowPublicCoordinates,
                minimumHomeAudience,
                defaultRevealOffset)
            : inherited;
        return resolved;
    }

    private static bool TryBoolean(
        IReadOnlyDictionary<string, string> rows,
        string key,
        bool inherited,
        out bool value)
    {
        value = inherited;
        if (!rows.TryGetValue(key, out string? json))
        {
            return true;
        }

        if (!TryReadJson(json, JsonValueKind.True, JsonValueKind.False, out JsonElement element))
        {
            return false;
        }

        value = element.GetBoolean();
        return true;
    }

    private static bool TryAudience(
        IReadOnlyDictionary<string, string> rows,
        LocationDisclosureAudienceEnum inherited,
        out LocationDisclosureAudienceEnum value)
    {
        value = inherited;
        if (!rows.TryGetValue(GovernanceSettingKeys.LocationPrivacy.MinimumHomeAudience, out string? json))
        {
            return true;
        }

        if (!TryReadJson(json, JsonValueKind.String, null, out JsonElement element))
        {
            return false;
        }

        value = element.GetString() switch
        {
            "NEVER" => LocationDisclosureAudienceEnum.Never,
            "CONFIRMED_PARTICIPANT" => LocationDisclosureAudienceEnum.ConfirmedParticipant,
            "ANY_CURRENT_REGISTRANT" => LocationDisclosureAudienceEnum.AnyCurrentRegistrant,
            _ => 0
        };
        return Enum.IsDefined(value);
    }

    private static bool TryDuration(
        IReadOnlyDictionary<string, string> rows,
        TimeSpan inherited,
        out TimeSpan value)
    {
        value = inherited;
        if (!rows.TryGetValue(GovernanceSettingKeys.LocationPrivacy.DefaultRevealOffset, out string? json)
            || TryReadJson(json, JsonValueKind.String, null, out JsonElement element)
            && TryParseDuration(element.GetString(), out value))
        {
            return true;
        }

        return false;
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

    private static bool TryReadJson(
        string json,
        JsonValueKind firstKind,
        JsonValueKind? secondKind,
        out JsonElement element)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            element = document.RootElement.Clone();
            return element.ValueKind == firstKind || element.ValueKind == secondKind;
        }
        catch (JsonException)
        {
            element = default;
            return false;
        }
    }

    private static LocationDisclosureAudienceEnum MoreRestrictive(
        LocationDisclosureAudienceEnum first,
        LocationDisclosureAudienceEnum second)
        => RestrictionRank(first) >= RestrictionRank(second) ? first : second;

    private static int RestrictionRank(LocationDisclosureAudienceEnum audience) => audience switch
    {
        LocationDisclosureAudienceEnum.AnyCurrentRegistrant => 1,
        LocationDisclosureAudienceEnum.ConfirmedParticipant => 2,
        LocationDisclosureAudienceEnum.Never => 3,
        _ => int.MaxValue
    };

    private static string ComputeVersion(IReadOnlyDictionary<string, string> values)
    {
        string canonical = string.Join(
            '\n',
            Keys.Select(key => $"{key}={values.GetValueOrDefault(key, "<inherited>")}"));
        return ComputeVersion(canonical);
    }

    private static string ComputeVersion(params string[] parts)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', parts)));
        return Convert.ToHexStringLower(hash);
    }

    private sealed record GovernanceValues(
        bool AllowHomeLocations,
        bool AllowPublicExactAddress,
        bool AllowPublicCoordinates,
        LocationDisclosureAudienceEnum MinimumHomeAudience,
        TimeSpan DefaultRevealOffset);
}
