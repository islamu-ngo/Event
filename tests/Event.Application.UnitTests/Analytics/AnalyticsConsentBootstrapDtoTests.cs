using Explore.Application.DTOs.Onboarding;

namespace Event.Application.UnitTests.Analytics;

public class AnalyticsConsentBootstrapDtoTests
{
    // --- Privacy-first defaults ---

    [Test]
    public async Task DefaultValues_CookieBannerDisabled()
    {
        var dto = new AnalyticsConsentBootstrapDto();

        await Assert.That(dto.CookieBannerEnabled).IsFalse();
    }

    [Test]
    public async Task DefaultValues_CannotRunBeforeConsent()
    {
        var dto = new AnalyticsConsentBootstrapDto();

        await Assert.That(dto.CanRunBeforeConsent).IsFalse();
    }

    [Test]
    public async Task DefaultValues_DeclineBehaviorIsDisable()
    {
        var dto = new AnalyticsConsentBootstrapDto();

        await Assert.That(dto.DeclineBehavior).IsEqualTo("disable");
    }

    [Test]
    public async Task DefaultValues_ConsentCookieKeyIsDefault()
    {
        var dto = new AnalyticsConsentBootstrapDto();

        await Assert.That(dto.ConsentCookieKey).IsEqualTo("explore_cc_default");
    }

    [Test]
    public async Task DefaultValues_ConsentCookieLifetimeIs180Days()
    {
        var dto = new AnalyticsConsentBootstrapDto();

        await Assert.That(dto.ConsentCookieLifetimeDays).IsEqualTo(180);
    }

    [Test]
    public async Task DefaultValues_AnalyticsProviderIsNone()
    {
        var dto = new AnalyticsConsentBootstrapDto();

        await Assert.That(dto.AnalyticsProvider).IsEqualTo("none");
    }

    [Test]
    public async Task DefaultValues_PosthogIsNull()
    {
        var dto = new AnalyticsConsentBootstrapDto();

        await Assert.That(dto.Posthog).IsNull();
    }

    // --- PosthogClientBootstrapDto privacy-first defaults ---

    [Test]
    public async Task PosthogDefaults_CookielessModeIsOff()
    {
        var dto = new PosthogClientBootstrapDto();

        await Assert.That(dto.CookielessMode).IsEqualTo("off");
    }

    [Test]
    public async Task PosthogDefaults_PersonProfilesIsIdentifiedOnly()
    {
        var dto = new PosthogClientBootstrapDto();

        await Assert.That(dto.PersonProfiles).IsEqualTo("identified_only");
    }

    [Test]
    public async Task PosthogDefaults_AllFeatureFlagsDisabled()
    {
        var dto = new PosthogClientBootstrapDto();

        await Assert.That(dto.SessionReplay).IsFalse();
        await Assert.That(dto.Autocapture).IsFalse();
        await Assert.That(dto.Heatmaps).IsFalse();
        await Assert.That(dto.Toolbar).IsFalse();
    }

    // --- No sensitive data fields (Amendment 3 compliance) ---

    [Test]
    public async Task BootstrapDto_DoesNotExposeSensitiveFields()
    {
        var properties = typeof(AnalyticsConsentBootstrapDto)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        await Assert.That(properties).DoesNotContain("PersonalApiKey");
        await Assert.That(properties).DoesNotContain("ApiKey");
        await Assert.That(properties).DoesNotContain("TenantStableKey");
        await Assert.That(properties).DoesNotContain("EndpointUrl");
    }

    [Test]
    public async Task PosthogBootstrapDto_DoesNotExposeSensitiveFields()
    {
        var properties = typeof(PosthogClientBootstrapDto)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        await Assert.That(properties).DoesNotContain("ApiKey");
        await Assert.That(properties).DoesNotContain("PersonalApiKey");
    }

    [Test]
    public async Task BootstrapDto_HasExactly7Properties()
    {
        var count = typeof(AnalyticsConsentBootstrapDto).GetProperties().Length;

        await Assert.That(count).IsEqualTo(7);
    }

    [Test]
    public async Task PosthogBootstrapDto_HasExactly6Properties()
    {
        var count = typeof(PosthogClientBootstrapDto).GetProperties().Length;

        await Assert.That(count).IsEqualTo(6);
    }
}
