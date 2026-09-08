using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Settings.Groups;
using Explore.Domain.Enums.Analytics;

namespace Event.Application.UnitTests.Settings.Groups;

public class AnalyticsSettingGroupTests
{
    private static IReadOnlyDictionary<string, ResolvedSetting> CreateSettings(
        params (string key, string value)[] entries)
    {
        return entries.ToDictionary(e => e.key, e => new ResolvedSetting { Value = e.value });
    }

    // --- Basic properties ---

    [Test]
    public async Task Populate_SetsProviderFromGovernanceKey()
    {
        var group = new AnalyticsSettingGroup();
        group.Populate(CreateSettings(("analytics.provider", "\"posthog\"")));

        await Assert.That(group.Provider).IsEqualTo("posthog");
        await Assert.That(group.ProviderEnum).IsEqualTo(Explore.Domain.Enums.AnalyticsProviderEnum.Posthog);
    }

    [Test]
    public async Task Populate_EnabledDefaultsFalse()
    {
        var group = new AnalyticsSettingGroup();
        group.Populate(new Dictionary<string, ResolvedSetting>());

        await Assert.That(group.Enabled).IsFalse();
    }

    // --- Cookie consent governance ---

    [Test]
    public async Task Populate_CookieConsentEnabled_ParsesCorrectly()
    {
        var group = new AnalyticsSettingGroup();
        group.Populate(CreateSettings(("analytics.cookie_consent_enabled", "true")));

        await Assert.That(group.CookieConsentEnabled).IsTrue();
    }

    [Test]
    public async Task Populate_GlobalDisableClientTracking_ParsesCorrectly()
    {
        var group = new AnalyticsSettingGroup();
        group.Populate(CreateSettings(("analytics.global_disable_client_tracking", "true")));

        await Assert.That(group.GlobalDisableClientTracking).IsTrue();
    }

    [Test]
    public async Task Populate_ConsentCookieLifetimeDays_ParsesCorrectly()
    {
        var group = new AnalyticsSettingGroup();
        group.Populate(CreateSettings(("analytics.consent_cookie_lifetime_days", "90")));

        await Assert.That(group.ConsentCookieLifetimeDays).IsEqualTo(90);
    }

    [Test]
    public async Task Populate_ConsentCookieLifetimeDays_DefaultsTo180()
    {
        var group = new AnalyticsSettingGroup();
        group.Populate(new Dictionary<string, ResolvedSetting>());

        await Assert.That(group.ConsentCookieLifetimeDays).IsEqualTo(180);
    }

    // --- Enum parsing from snake_case ---

    [Test]
    public async Task Populate_DeclineBehavior_ParsesSnakeCase()
    {
        var group = new AnalyticsSettingGroup();
        group.Populate(CreateSettings(("analytics.decline_behavior", "\"cookieless\"")));

        await Assert.That(group.DeclineBehavior).IsEqualTo(DeclineBehavior.Cookieless);
    }

    [Test]
    public async Task Populate_DeclineBehavior_ParsesDisable()
    {
        var group = new AnalyticsSettingGroup();
        group.Populate(CreateSettings(("analytics.decline_behavior", "\"disable\"")));

        await Assert.That(group.DeclineBehavior).IsEqualTo(DeclineBehavior.Disable);
    }

    [Test]
    public async Task Populate_DeclineBehavior_DefaultsToCookieless()
    {
        var group = new AnalyticsSettingGroup();
        group.Populate(new Dictionary<string, ResolvedSetting>());

        await Assert.That(group.DeclineBehavior).IsEqualTo(DeclineBehavior.Cookieless);
    }

    // --- PostHog cookieless mode parsing ---

    [Test]
    public async Task Populate_PosthogCookielessMode_ParsesAlways()
    {
        var group = new AnalyticsSettingGroup();
        group.Populate(CreateSettings(("analytics.posthog_cookieless_mode", "\"always\"")));

        await Assert.That(group.PosthogCookielessMode).IsEqualTo(PosthogCookielessMode.Always);
    }

    [Test]
    public async Task Populate_PosthogCookielessMode_ParsesOnReject()
    {
        var group = new AnalyticsSettingGroup();
        group.Populate(CreateSettings(("analytics.posthog_cookieless_mode", "\"on_reject\"")));

        await Assert.That(group.PosthogCookielessMode).IsEqualTo(PosthogCookielessMode.OnReject);
    }

    [Test]
    public async Task Populate_PosthogCookielessMode_ParsesOff()
    {
        var group = new AnalyticsSettingGroup();
        group.Populate(CreateSettings(("analytics.posthog_cookieless_mode", "\"off\"")));

        await Assert.That(group.PosthogCookielessMode).IsEqualTo(PosthogCookielessMode.Off);
    }

    [Test]
    public async Task Populate_PosthogCookielessMode_DefaultsToOnReject()
    {
        var group = new AnalyticsSettingGroup();
        group.Populate(new Dictionary<string, ResolvedSetting>());

        await Assert.That(group.PosthogCookielessMode).IsEqualTo(PosthogCookielessMode.OnReject);
    }

    // --- PostHog person profiles ---

    [Test]
    public async Task Populate_PosthogPersonProfiles_ParsesIdentifiedOnly()
    {
        var group = new AnalyticsSettingGroup();
        group.Populate(CreateSettings(("analytics.posthog_person_profiles", "\"identified_only\"")));

        await Assert.That(group.PosthogPersonProfiles).IsEqualTo(PosthogPersonProfiles.IdentifiedOnly);
    }

    [Test]
    public async Task Populate_PosthogPersonProfiles_ParsesNever()
    {
        var group = new AnalyticsSettingGroup();
        group.Populate(CreateSettings(("analytics.posthog_person_profiles", "\"never\"")));

        await Assert.That(group.PosthogPersonProfiles).IsEqualTo(PosthogPersonProfiles.Never);
    }

    [Test]
    public async Task Populate_PosthogPersonProfiles_ParsesAlways()
    {
        var group = new AnalyticsSettingGroup();
        group.Populate(CreateSettings(("analytics.posthog_person_profiles", "\"always\"")));

        await Assert.That(group.PosthogPersonProfiles).IsEqualTo(PosthogPersonProfiles.Always);
    }

    // --- PostHog feature flags ---

    [Test]
    public async Task Populate_PosthogFeatureFlags_DefaultToFalse()
    {
        var group = new AnalyticsSettingGroup();
        group.Populate(new Dictionary<string, ResolvedSetting>());

        await Assert.That(group.PosthogSessionReplay).IsFalse();
        await Assert.That(group.PosthogAutocapture).IsFalse();
        await Assert.That(group.PosthogHeatmaps).IsFalse();
        await Assert.That(group.PosthogToolbar).IsFalse();
    }

    [Test]
    public async Task Populate_PosthogSessionReplay_Enabled()
    {
        var group = new AnalyticsSettingGroup();
        group.Populate(CreateSettings(("analytics.posthog_session_replay", "true")));

        await Assert.That(group.PosthogSessionReplay).IsTrue();
    }

    // --- ProviderEnum parsing ---

    [Test]
    public async Task ProviderEnum_UnknownProvider_FallsBackToNone()
    {
        var group = new AnalyticsSettingGroup();
        group.Populate(CreateSettings(("analytics.provider", "\"unknown_provider\"")));

        await Assert.That(group.ProviderEnum).IsEqualTo(Explore.Domain.Enums.AnalyticsProviderEnum.None);
    }

    // --- TenantStableKey ---

    [Test]
    public async Task TenantStableKey_IsSettableExternally()
    {
        var group = new AnalyticsSettingGroup();
        group.TenantStableKey = "a1b2c3d4";

        await Assert.That(group.TenantStableKey).IsEqualTo("a1b2c3d4");
    }

    // --- SettingKeys completeness ---

    [Test]
    public async Task SettingKeys_Contains16Keys()
    {
        var keys = AnalyticsSettingGroup.SettingKeys.ToList();

        await Assert.That(keys.Count).IsEqualTo(16);
    }
}
