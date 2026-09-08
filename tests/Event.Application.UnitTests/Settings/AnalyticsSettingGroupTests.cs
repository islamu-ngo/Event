namespace Event.Application.UnitTests.Settings;

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Settings.Groups;
using Explore.Domain;
using Explore.Domain.Constants;

public class AnalyticsSettingGroupTests
{
    [Test]
    public async Task SettingKeys_UseCanonicalAnalyticsGovernanceKeys()
    {
        var keys = AnalyticsSettingGroup.SettingKeys.ToArray();

        var expected = new[]
        {
            GovernanceSettingKeys.Analytics.Provider,
            GovernanceSettingKeys.Analytics.ConsentMode,
            GovernanceSettingKeys.Analytics.TransportMode,
            GovernanceSettingKeys.Analytics.EndpointUrl,
            GovernanceSettingKeys.Analytics.ApiKey,
            GovernanceSettingKeys.Analytics.Enabled,
            GovernanceSettingKeys.Analytics.CookieConsentEnabled,
            GovernanceSettingKeys.Analytics.DeclineBehavior,
            GovernanceSettingKeys.Analytics.ConsentCookieLifetimeDays,
            GovernanceSettingKeys.Analytics.GlobalDisableClientTracking,
            GovernanceSettingKeys.Analytics.PosthogCookielessMode,
            GovernanceSettingKeys.Analytics.PosthogPersonProfiles,
            GovernanceSettingKeys.Analytics.PosthogSessionReplay,
            GovernanceSettingKeys.Analytics.PosthogAutocapture,
            GovernanceSettingKeys.Analytics.PosthogHeatmaps,
            GovernanceSettingKeys.Analytics.PosthogToolbar
        };

        await Assert.That(keys.SequenceEqual(expected)).IsTrue();
    }

    [Test]
    public async Task Populate_WithCanonicalKeys_MapsAllSupportedProperties()
    {
        var settings = new Dictionary<string, ResolvedSetting>
        {
            [GovernanceSettingKeys.Analytics.Provider] = CreateResolvedSetting(GovernanceSettingKeys.Analytics.Provider, "\"posthog\"", SettingValueType.String),
            [GovernanceSettingKeys.Analytics.ConsentMode] = CreateResolvedSetting(GovernanceSettingKeys.Analytics.ConsentMode, "\"identified\"", SettingValueType.String),
            [GovernanceSettingKeys.Analytics.TransportMode] = CreateResolvedSetting(GovernanceSettingKeys.Analytics.TransportMode, "\"relay\"", SettingValueType.String),
            [GovernanceSettingKeys.Analytics.EndpointUrl] = CreateResolvedSetting(GovernanceSettingKeys.Analytics.EndpointUrl, "\"https://analytics.example.com\"", SettingValueType.String),
            [GovernanceSettingKeys.Analytics.ApiKey] = CreateResolvedSetting(GovernanceSettingKeys.Analytics.ApiKey, "\"pk_live\"", SettingValueType.String),
            [GovernanceSettingKeys.Analytics.Enabled] = CreateResolvedSetting(GovernanceSettingKeys.Analytics.Enabled, "true", SettingValueType.Boolean)
        };

        var group = new AnalyticsSettingGroup();

        group.Populate(settings);

        await Assert.That(group.Provider).IsEqualTo("posthog");
        await Assert.That(group.ConsentMode).IsEqualTo("identified");
        await Assert.That(group.TransportMode).IsEqualTo("relay");
        await Assert.That(group.EndpointUrl).IsEqualTo("https://analytics.example.com");
        await Assert.That(group.ApiKey).IsEqualTo("pk_live");
        await Assert.That(group.Enabled).IsTrue();
    }

    [Test]
    public async Task Populate_WithLegacyKeys_DoesNotPopulateCanonicalProperties()
    {
        var settings = new Dictionary<string, ResolvedSetting>
        {
            ["analytics.endpoint"] = CreateResolvedSetting("analytics.endpoint", "\"https://legacy.example.com\"", SettingValueType.String),
            ["analytics.site_id"] = CreateResolvedSetting("analytics.site_id", "\"legacy-site\"", SettingValueType.String)
        };

        var group = new AnalyticsSettingGroup();

        group.Populate(settings);

        await Assert.That(group.EndpointUrl).IsNull();
        await Assert.That(group.ApiKey).IsNull();
        await Assert.That(group.Provider).IsEqualTo("none");
        await Assert.That(group.ConsentMode).IsEqualTo("pseudonymous");
        await Assert.That(group.TransportMode).IsEqualTo("direct");
        await Assert.That(group.Enabled).IsFalse();
    }

    private static ResolvedSetting CreateResolvedSetting(string key, string value, SettingValueType valueType)
    {
        return new ResolvedSetting
        {
            Key = key,
            Value = value,
            ValueType = valueType,
            Source = SettingSource.SystemDefault,
            IsLocked = false
        };
    }
}
