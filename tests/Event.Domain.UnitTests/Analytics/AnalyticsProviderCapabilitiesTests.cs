using Explore.Domain.Analytics;
using Explore.Domain.Enums;

namespace Event.Domain.UnitTests.Analytics;

public class AnalyticsProviderCapabilitiesTests
{
    [Test]
    public async Task For_Posthog_SupportsCookielessAndConsentTransition()
    {
        var caps = AnalyticsProviderCapabilities.For(AnalyticsProviderEnum.Posthog);

        await Assert.That(caps.SupportsCookielessMode).IsTrue();
        await Assert.That(caps.SupportsNativeConsentTransition).IsTrue();
        await Assert.That(caps.SupportsPersonProfiles).IsTrue();
        await Assert.That(caps.RequiresClientApiKey).IsTrue();
        await Assert.That(caps.InherentlyCookieless).IsFalse();
    }

    [Test]
    public async Task For_Plausible_IsInherentlyCookieless()
    {
        var caps = AnalyticsProviderCapabilities.For(AnalyticsProviderEnum.Plausible);

        await Assert.That(caps.InherentlyCookieless).IsTrue();
        await Assert.That(caps.SupportsCookielessMode).IsFalse();
        await Assert.That(caps.RequiresClientApiKey).IsFalse();
    }

    [Test]
    public async Task For_Rybbit_IsInherentlyCookieless()
    {
        var caps = AnalyticsProviderCapabilities.For(AnalyticsProviderEnum.Rybbit);

        await Assert.That(caps.InherentlyCookieless).IsTrue();
        await Assert.That(caps.SupportsCookielessMode).IsFalse();
        await Assert.That(caps.RequiresClientApiKey).IsFalse();
    }

    [Test]
    public async Task For_RudderStack_RequiresClientApiKey_NotInherentlyCookieless()
    {
        var caps = AnalyticsProviderCapabilities.For(AnalyticsProviderEnum.RudderStack);

        await Assert.That(caps.InherentlyCookieless).IsFalse();
        await Assert.That(caps.SupportsCookielessMode).IsFalse();
        await Assert.That(caps.RequiresClientApiKey).IsTrue();
    }

    [Test]
    public async Task For_None_IsInherentlyCookieless()
    {
        var caps = AnalyticsProviderCapabilities.For(AnalyticsProviderEnum.None);

        await Assert.That(caps.InherentlyCookieless).IsTrue();
        await Assert.That(caps.SupportsCookielessMode).IsFalse();
        await Assert.That(caps.RequiresClientApiKey).IsFalse();
    }
}
