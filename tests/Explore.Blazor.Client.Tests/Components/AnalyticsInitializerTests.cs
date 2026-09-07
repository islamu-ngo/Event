using Bunit;
using Explore.Blazor.Client.Models.Analytics;
using Explore.Blazor.Client.Shared;

namespace Explore.Blazor.Client.Tests.Components;

public class AnalyticsInitializerTests : IDisposable
{
    private const string NavigationSourceProperty = "navigation_source";
    private const string TenantIdProperty = "tenant_id";
    private const string PageReferrerProperty = "page_referrer";

    private readonly BlazorTestContext _ctx;
    private readonly ICookieConsentInterop _cookieInterop;
    private readonly CookieConsentStateService _consentStateService;

    public AnalyticsInitializerTests()
    {
        _ctx = new BlazorTestContext();
        _cookieInterop = Substitute.For<ICookieConsentInterop>();
        _ctx.Services.AddSingleton(_cookieInterop);

        _consentStateService = new CookieConsentStateService();
        _ctx.Services.AddSingleton(_consentStateService);
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    private static bool MatchesInitialPageViewProperties(IDictionary<string, object>? properties, Guid tenantId)
    {
        return MatchesPageViewProperties(properties, "initial_load", tenantId.ToString(), null, 2);
    }

    private static bool MatchesProgrammaticPageViewProperties(IDictionary<string, object>? properties, Guid tenantId, string pageReferrer)
    {
        return MatchesPageViewProperties(properties, "programmatic_navigation", tenantId.ToString(), pageReferrer, 3);
    }

    private static bool MatchesPageViewProperties(
        IDictionary<string, object>? properties,
        string expectedNavigationSource,
        string? expectedTenantId,
        string? expectedPageReferrer,
        int expectedCount)
    {
        if (properties is null || properties.Count != expectedCount)
        {
            return false;
        }

        if (!TryGetString(properties, NavigationSourceProperty, out var navigationSource)
            || !string.Equals(navigationSource, expectedNavigationSource, StringComparison.Ordinal))
        {
            return false;
        }

        if (expectedTenantId is null)
        {
            if (properties.ContainsKey(TenantIdProperty))
            {
                return false;
            }
        }
        else if (!TryGetString(properties, TenantIdProperty, out var tenantId)
            || !string.Equals(tenantId, expectedTenantId, StringComparison.Ordinal))
        {
            return false;
        }

        if (expectedPageReferrer is null)
        {
            return !properties.ContainsKey(PageReferrerProperty);
        }

        return TryGetString(properties, PageReferrerProperty, out var pageReferrer)
            && string.Equals(pageReferrer, expectedPageReferrer, StringComparison.Ordinal);
    }

    private static bool TryGetString(IDictionary<string, object> properties, string key, out string? value)
    {
        value = null;

        if (!properties.TryGetValue(key, out var rawValue))
        {
            return false;
        }

        value = rawValue?.ToString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private IRenderedComponent<AnalyticsInitializer> RenderAnalyticsInitializer() =>
        _ctx.Render<AnalyticsInitializer>();

    [Test]
    public async Task Renders_WithValidSettings_InitializesInteropAndTracksInitialPageView()
    {
        var tenantId = Guid.NewGuid();
        var settingsService = Substitute.For<IPublicExperienceService>();
        settingsService.GetSettingsAsync().Returns(new PublicExperienceSettingsDto
        {
            TenantId = tenantId,
            AnalyticsProvider = "posthog",
            AnalyticsEnabled = true,
            AnalyticsConsentMode = "identified",
            AnalyticsTransportMode = "relay",
            AnalyticsAllowIdentify = true,
            AnalyticsPublicApiKey = "public-key",
            AnalyticsEndpointUrl = "https://analytics.example.com"
        });

        var analyticsInterop = Substitute.For<IAnalyticsInterop>();
        _ctx.Services.AddSingleton(settingsService);
        _ctx.Services.AddSingleton(analyticsInterop);

        var cut = RenderAnalyticsInitializer();
        await Task.Yield();

        cut.WaitForAssertion(() =>
        {
            analyticsInterop.Received(1).InitAsync("posthog", true, "identified", "relay", true, "public-key", "https://analytics.example.com");
            analyticsInterop.Received(1).PageViewAsync(
                "/",
                Arg.Is<IDictionary<string, object>>(properties => MatchesInitialPageViewProperties(properties, tenantId)));
        });
    }

    [Test]
    public async Task Renders_WithNullSettings_DoesNotInitializeInterop()
    {
        var settingsService = Substitute.For<IPublicExperienceService>();
        settingsService.GetSettingsAsync().Returns((PublicExperienceSettingsDto?)null);

        var analyticsInterop = Substitute.For<IAnalyticsInterop>();
        _ctx.Services.AddSingleton(settingsService);
        _ctx.Services.AddSingleton(analyticsInterop);

        RenderAnalyticsInitializer();
        await Task.Yield();

        await analyticsInterop.DidNotReceiveWithAnyArgs().InitAsync(default!, default, default!, default!, default, default!, default!);
    }

    [Test]
    public async Task Renders_WithAnalyticsDisabled_DoesNotTrackPageViews()
    {
        var settingsService = Substitute.For<IPublicExperienceService>();
        settingsService.GetSettingsAsync().Returns(new PublicExperienceSettingsDto
        {
            AnalyticsProvider = "none",
            AnalyticsEnabled = false,
            AnalyticsConsentMode = "anonymous",
            AnalyticsTransportMode = "direct",
            AnalyticsAllowIdentify = false
        });

        var analyticsInterop = Substitute.For<IAnalyticsInterop>();
        _ctx.Services.AddSingleton(settingsService);
        _ctx.Services.AddSingleton(analyticsInterop);

        var cut = RenderAnalyticsInitializer();
        await Task.Yield();

        cut.WaitForAssertion(() =>
        {
            analyticsInterop.DidNotReceiveWithAnyArgs().InitAsync(default!, default, default!, default!, default, default!, default!);
            analyticsInterop.DidNotReceiveWithAnyArgs().PageViewAsync(default!, default!);
        });
    }

    [Test]
    public async Task Navigates_ToNewRoute_TracksProgrammaticPageViewWithReferrer()
    {
        var tenantId = Guid.NewGuid();
        var settingsService = Substitute.For<IPublicExperienceService>();
        settingsService.GetSettingsAsync().Returns(new PublicExperienceSettingsDto
        {
            TenantId = tenantId,
            AnalyticsProvider = "posthog",
            AnalyticsEnabled = true,
            AnalyticsConsentMode = "pseudonymous",
            AnalyticsTransportMode = "relay",
            AnalyticsAllowIdentify = false,
            AnalyticsEndpointUrl = "/api/a/t"
        });

        var analyticsInterop = Substitute.For<IAnalyticsInterop>();
        _ctx.Services.AddSingleton(settingsService);
        _ctx.Services.AddSingleton(analyticsInterop);
        var navigationManager = _ctx.Services.GetRequiredService<BunitNavigationManager>();

        var cut = RenderAnalyticsInitializer();
        await Task.Yield();

        cut.WaitForAssertion(() =>
        {
            analyticsInterop.Received(1).PageViewAsync(
                "/",
                Arg.Is<IDictionary<string, object>>(properties => MatchesInitialPageViewProperties(properties, tenantId)));
        });

        navigationManager.NavigateTo("/events");

        cut.WaitForAssertion(() =>
        {
            analyticsInterop.Received(1).PageViewAsync(
                "/events",
                Arg.Is<IDictionary<string, object>>(properties => MatchesProgrammaticPageViewProperties(properties, tenantId, "/")));
        });
    }

    [Test]
    public async Task Renders_WhenSettingsServiceThrows_DoesNotCrash()
    {
        var settingsService = Substitute.For<IPublicExperienceService>();
        settingsService.GetSettingsAsync().ThrowsAsync(new InvalidOperationException("settings unavailable"));

        var analyticsInterop = Substitute.For<IAnalyticsInterop>();
        _ctx.Services.AddSingleton(settingsService);
        _ctx.Services.AddSingleton(analyticsInterop);

        RenderAnalyticsInitializer();
        await Task.Yield();

        await analyticsInterop.DidNotReceiveWithAnyArgs().InitAsync(default!, default, default!, default!, default, default!, default!);
    }

    private static PublicExperienceSettingsDto CreateSettingsWithConsent(
        bool cookieBannerEnabled = true,
        bool canRunBeforeConsent = true,
        string declineBehavior = "cookieless",
        string consentCookieKey = "explore_cc_test",
        int cookieLifetimeDays = 180,
        PosthogClientBootstrapDto? posthog = null)
    {
        return new PublicExperienceSettingsDto
        {
            TenantId = Guid.NewGuid(),
            AnalyticsProvider = "posthog",
            AnalyticsEnabled = true,
            AnalyticsConsentMode = "identified",
            AnalyticsTransportMode = "relay",
            AnalyticsAllowIdentify = true,
            AnalyticsPublicApiKey = "phc_test123",
            AnalyticsEndpointUrl = "https://analytics.example.com",
            AnalyticsConsent = new AnalyticsConsentBootstrapDto
            {
                CookieBannerEnabled = cookieBannerEnabled,
                CanRunBeforeConsent = canRunBeforeConsent,
                DeclineBehavior = declineBehavior,
                ConsentCookieKey = consentCookieKey,
                ConsentCookieLifetimeDays = cookieLifetimeDays,
                AnalyticsProvider = "posthog",
                Posthog = posthog
            }
        };
    }

    [Test]
    public async Task BannerEnabled_NoExistingCookie_CanRunBeforeConsent_ShowsBannerAndInitsCookieless()
    {
        var posthog = new PosthogClientBootstrapDto { CookielessMode = "on_reject", PersonProfiles = "identified_only" };
        var settings = CreateSettingsWithConsent(canRunBeforeConsent: true, posthog: posthog);
        var settingsService = Substitute.For<IPublicExperienceService>();
        settingsService.GetSettingsAsync().Returns(settings);
        _cookieInterop.ReadConsentAsync(Arg.Any<string>()).Returns((string?)null);
        var analyticsInterop = Substitute.For<IAnalyticsInterop>();
        _ctx.Services.AddSingleton(settingsService);
        _ctx.Services.AddSingleton(analyticsInterop);

        var cut = RenderAnalyticsInitializer();
        await Task.Yield();

        cut.WaitForAssertion(() =>
        {
            cut.Find(".cookie-consent-banner");
            analyticsInterop.Received(1).InitAsync(
                "posthog", true, "identified", "relay", true, "phc_test123", "https://analytics.example.com",
                Arg.Is<Explore.Blazor.Client.Models.Analytics.PosthogClientBootstrapModel?>(
                    p => p != null && p.CookielessMode == "always"));
            analyticsInterop.Received(1).PageViewAsync("/", Arg.Any<IDictionary<string, object>>());
        });
    }

    [Test]
    public async Task BannerEnabled_NoExistingCookie_CannotRunBeforeConsent_ShowsBannerAndBlocksAnalytics()
    {
        var settings = CreateSettingsWithConsent(canRunBeforeConsent: false);
        var settingsService = Substitute.For<IPublicExperienceService>();
        settingsService.GetSettingsAsync().Returns(settings);
        _cookieInterop.ReadConsentAsync(Arg.Any<string>()).Returns((string?)null);
        var analyticsInterop = Substitute.For<IAnalyticsInterop>();
        _ctx.Services.AddSingleton(settingsService);
        _ctx.Services.AddSingleton(analyticsInterop);

        var cut = RenderAnalyticsInitializer();
        await Task.Yield();

        cut.WaitForAssertion(() =>
        {
            cut.Find(".cookie-consent-banner");
            analyticsInterop.DidNotReceiveWithAnyArgs().InitAsync(default!, default, default!, default!, default, default!, default!);
            analyticsInterop.DidNotReceiveWithAnyArgs().PageViewAsync(default!, default!);
        });
    }

    [Test]
    public async Task BannerEnabled_ExistingAcceptedCookie_HidesBannerAndInitsFullAnalytics()
    {
        var settings = CreateSettingsWithConsent();
        var settingsService = Substitute.For<IPublicExperienceService>();
        settingsService.GetSettingsAsync().Returns(settings);
        _cookieInterop.ReadConsentAsync(Arg.Any<string>()).Returns("accepted");
        var analyticsInterop = Substitute.For<IAnalyticsInterop>();
        _ctx.Services.AddSingleton(settingsService);
        _ctx.Services.AddSingleton(analyticsInterop);

        var cut = RenderAnalyticsInitializer();
        await Task.Yield();

        cut.WaitForAssertion(() =>
        {
            if (cut.FindAll(".cookie-consent-banner").Count > 0)
                throw new InvalidOperationException("Banner should not be visible after accepted cookie");

            analyticsInterop.Received(1).OptInCapturingAsync();
            analyticsInterop.Received(1).InitAsync(
                "posthog", true, "identified", "relay", true, "phc_test123", "https://analytics.example.com",
                Arg.Any<Explore.Blazor.Client.Models.Analytics.PosthogClientBootstrapModel?>());
            analyticsInterop.Received(1).PageViewAsync("/", Arg.Any<IDictionary<string, object>>());
        });
    }

    [Test]
    public async Task BannerEnabled_ExistingDeclinedCookie_CookielessBehavior_OptsOutAndTracksPageViews()
    {
        var settings = CreateSettingsWithConsent(declineBehavior: "cookieless");
        var settingsService = Substitute.For<IPublicExperienceService>();
        settingsService.GetSettingsAsync().Returns(settings);
        _cookieInterop.ReadConsentAsync(Arg.Any<string>()).Returns("declined");
        var analyticsInterop = Substitute.For<IAnalyticsInterop>();
        _ctx.Services.AddSingleton(settingsService);
        _ctx.Services.AddSingleton(analyticsInterop);

        var cut = RenderAnalyticsInitializer();
        await Task.Yield();

        cut.WaitForAssertion(() =>
        {
            if (cut.FindAll(".cookie-consent-banner").Count > 0)
                throw new InvalidOperationException("Banner should not be visible after declined cookie");

            analyticsInterop.Received(1).OptOutCapturingAsync();
            analyticsInterop.Received(1).PageViewAsync("/", Arg.Any<IDictionary<string, object>>());
        });
    }

    [Test]
    public async Task BannerEnabled_ExistingDeclinedCookie_DisableBehavior_StopsAllAnalytics()
    {
        var settings = CreateSettingsWithConsent(declineBehavior: "disable");
        var settingsService = Substitute.For<IPublicExperienceService>();
        settingsService.GetSettingsAsync().Returns(settings);
        _cookieInterop.ReadConsentAsync(Arg.Any<string>()).Returns("declined");
        var analyticsInterop = Substitute.For<IAnalyticsInterop>();
        _ctx.Services.AddSingleton(settingsService);
        _ctx.Services.AddSingleton(analyticsInterop);

        var cut = RenderAnalyticsInitializer();
        await Task.Yield();

        cut.WaitForAssertion(() =>
        {
            if (cut.FindAll(".cookie-consent-banner").Count > 0)
                throw new InvalidOperationException("Banner should not be visible");

            analyticsInterop.DidNotReceiveWithAnyArgs().InitAsync(default!, default, default!, default!, default, default!, default!);
            analyticsInterop.DidNotReceiveWithAnyArgs().PageViewAsync(default!, default!);
        });
    }

    [Test]
    public async Task AcceptCallback_WritesCookieAndInitsFullAnalytics()
    {
        var settings = CreateSettingsWithConsent(canRunBeforeConsent: true);
        var settingsService = Substitute.For<IPublicExperienceService>();
        settingsService.GetSettingsAsync().Returns(settings);
        _cookieInterop.ReadConsentAsync(Arg.Any<string>()).Returns((string?)null);
        var analyticsInterop = Substitute.For<IAnalyticsInterop>();
        _ctx.Services.AddSingleton(settingsService);
        _ctx.Services.AddSingleton(analyticsInterop);

        var cut = RenderAnalyticsInitializer();
        await Task.Yield();

        cut.WaitForAssertion(() => cut.Find(".cookie-consent-banner"));

        var acceptButton = cut.FindAll(".cookie-consent-banner__btn")
            .First(b => b.TextContent.Contains("Accept", StringComparison.OrdinalIgnoreCase));
        acceptButton.Click();

        cut.WaitForAssertion(() =>
        {
            _cookieInterop.Received(1).WriteConsentAsync("explore_cc_test", "accepted", 180);
            analyticsInterop.Received(1).OptInCapturingAsync();

            if (cut.FindAll(".cookie-consent-banner").Count > 0)
                throw new InvalidOperationException("Banner should be hidden after accept");
        });
    }

    [Test]
    public async Task DeclineCallback_CookielessBehavior_WritesCookieAndOptsOut()
    {
        var settings = CreateSettingsWithConsent(canRunBeforeConsent: true, declineBehavior: "cookieless");
        var settingsService = Substitute.For<IPublicExperienceService>();
        settingsService.GetSettingsAsync().Returns(settings);
        _cookieInterop.ReadConsentAsync(Arg.Any<string>()).Returns((string?)null);
        var analyticsInterop = Substitute.For<IAnalyticsInterop>();
        _ctx.Services.AddSingleton(settingsService);
        _ctx.Services.AddSingleton(analyticsInterop);

        var cut = RenderAnalyticsInitializer();
        await Task.Yield();

        cut.WaitForAssertion(() => cut.Find(".cookie-consent-banner"));

        var declineButton = cut.FindAll(".cookie-consent-banner__btn")
            .First(b => b.TextContent.Contains("Decline", StringComparison.OrdinalIgnoreCase));
        declineButton.Click();

        cut.WaitForAssertion(() =>
        {
            _cookieInterop.Received(1).WriteConsentAsync("explore_cc_test", "declined", 180);
            analyticsInterop.Received(1).OptOutCapturingAsync();

            if (cut.FindAll(".cookie-consent-banner").Count > 0)
                throw new InvalidOperationException("Banner should be hidden after decline");
        });
    }

    [Test]
    public async Task FooterReopen_ClearsCookieAndReShowsBanner()
    {
        var settings = CreateSettingsWithConsent(canRunBeforeConsent: true);
        var settingsService = Substitute.For<IPublicExperienceService>();
        settingsService.GetSettingsAsync().Returns(settings);
        _cookieInterop.ReadConsentAsync(Arg.Any<string>()).Returns("accepted");
        var analyticsInterop = Substitute.For<IAnalyticsInterop>();
        _ctx.Services.AddSingleton(settingsService);
        _ctx.Services.AddSingleton(analyticsInterop);

        var cut = RenderAnalyticsInitializer();
        await Task.Yield();

        cut.WaitForAssertion(() =>
        {
            if (cut.FindAll(".cookie-consent-banner").Count > 0)
                throw new InvalidOperationException("Banner should not be visible initially");
            analyticsInterop.Received(1).OptInCapturingAsync();
        });

        await _consentStateService.RequestReopenAsync();

        cut.WaitForAssertion(() =>
        {
            _cookieInterop.Received(1).ClearConsentAsync("explore_cc_test");
            cut.Find(".cookie-consent-banner");
        });
    }
}
