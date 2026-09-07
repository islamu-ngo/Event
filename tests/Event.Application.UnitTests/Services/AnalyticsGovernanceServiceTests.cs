using Explore.Application.Analytics;
using Explore.Application.Models;
using Explore.Application.Services;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Services;

public class AnalyticsGovernanceServiceTests
{
    [Test]
    public async Task CreateTrackRequest_WithPseudonymousMode_HashesDistinctIdAndFiltersUnknownProperties()
    {
        var service = new AnalyticsGovernanceService();
        var configuration = new AnalyticsConfiguration
        {
            Provider = AnalyticsProviderEnum.Posthog,
            IsEnabled = true,
            ConsentMode = AnalyticsConsentMode.Pseudonymous
        };

        var request = service.CreateTrackRequest(
            configuration,
            "user-123",
            AnalyticsEvents.TenantOnboarding.StepCompleted,
            new Dictionary<string, object?>
            {
                [AnalyticsEvents.Properties.TenantId] = Guid.Parse("018e4e5c-7f00-7000-8000-000000000111"),
                [AnalyticsEvents.Properties.StepIndex] = 2,
                [AnalyticsEvents.Properties.StepName] = "branding",
                [AnalyticsEvents.Properties.TotalSteps] = 6,
                [AnalyticsEvents.Properties.CompletedSteps] = new[] { "welcome", "branding" },
                ["ignored_property"] = "nope",
                ["email"] = "sensitive@example.com"
            });

        await Assert.That(request).IsNotNull();
        await Assert.That(request!.DistinctId).StartsWith("pseudo-");
        await Assert.That(request.EventName).IsEqualTo("onboarding.step_completed");
        await Assert.That(request.Properties.Keys).Contains(AnalyticsEvents.Properties.TenantId);
        await Assert.That(request.Properties.ContainsKey("ignored_property")).IsFalse();
        await Assert.That(request.Properties.ContainsKey("email")).IsFalse();
    }

    [Test]
    public async Task CreateTrackRequest_WithAnonymousMode_UsesAnonymousDistinctId()
    {
        var service = new AnalyticsGovernanceService();
        var configuration = new AnalyticsConfiguration
        {
            Provider = AnalyticsProviderEnum.Posthog,
            IsEnabled = true,
            ConsentMode = AnalyticsConsentMode.Anonymous
        };

        var request = service.CreateTrackRequest(
            configuration,
            "user-123",
            AnalyticsEvents.TenantOnboarding.StepCompleted,
            new Dictionary<string, object?>
            {
                [AnalyticsEvents.Properties.StepIndex] = 1
            });

        await Assert.That(request).IsNotNull();
        await Assert.That(request!.DistinctId).StartsWith("anonymous-");
    }

    [Test]
    public async Task AllowsIdentify_OnlyForIdentifiedModeAndSupportingProviders()
    {
        var service = new AnalyticsGovernanceService();

        await Assert.That(service.AllowsIdentify(AnalyticsProviderEnum.Posthog, AnalyticsConsentMode.Identified)).IsTrue();
        await Assert.That(service.AllowsIdentify(AnalyticsProviderEnum.Plausible, AnalyticsConsentMode.Identified)).IsFalse();
        await Assert.That(service.AllowsIdentify(AnalyticsProviderEnum.Posthog, AnalyticsConsentMode.Pseudonymous)).IsFalse();
    }
}
