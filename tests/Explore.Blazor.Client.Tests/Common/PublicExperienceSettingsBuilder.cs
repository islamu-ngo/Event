using Explore.Blazor.Client.Services;

namespace Explore.Blazor.Client.Tests.Common;

/// <summary>
/// Fluent builder for constructing <see cref="PublicExperienceSettingsDto"/> in tests.
/// Provides sensible defaults matching single-tenant production behavior.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// var settings = new PublicExperienceSettingsBuilder()
///     .WithBranding("My Event Hub", "/logo.png")
///     .WithIslamicModule()
///     .WithHomePage("LandingPage")
///     .Build();
/// </code>
/// </remarks>
public sealed class PublicExperienceSettingsBuilder
{
    private PublicExperienceSettingsDto _model = new();

    // ── Tenant Identity ─────────────────────────────────────────────────

    /// <summary>Sets the tenant ID for the settings.</summary>
    public PublicExperienceSettingsBuilder WithTenantId(Guid tenantId)
    {
        _model = _model with { TenantId = tenantId };
        return this;
    }

    /// <summary>Sets deployment mode. Common values: "SingleTenant", "MultiTenant".</summary>
    public PublicExperienceSettingsBuilder WithDeploymentMode(string mode)
    {
        _model = _model with { DeploymentMode = mode };
        return this;
    }

    // ── Branding ────────────────────────────────────────────────────────

    /// <summary>Sets brand display name and optional logo URL.</summary>
    public PublicExperienceSettingsBuilder WithBranding(string displayName, string? logoUrl = null)
    {
        _model = _model with { BrandDisplayName = displayName };
        if (logoUrl is not null)
            _model = _model with { BrandLogoUrl = logoUrl };
        return this;
    }

    /// <summary>Sets the brand favicon URL.</summary>
    public PublicExperienceSettingsBuilder WithFavicon(string faviconUrl)
    {
        _model = _model with { BrandFaviconUrl = faviconUrl };
        return this;
    }

    /// <summary>Sets a custom CSS URL for tenant white-labeling.</summary>
    public PublicExperienceSettingsBuilder WithCustomCss(string cssUrl)
    {
        _model = _model with { BrandCustomCssUrl = cssUrl };
        return this;
    }

    // ── Domain Configuration ────────────────────────────────────────────

    /// <summary>Sets the instance base domain (e.g., "explore.islamu.org").</summary>
    public PublicExperienceSettingsBuilder WithBaseDomain(string baseDomain)
    {
        _model = _model with { InstanceBaseDomain = baseDomain };
        return this;
    }

    /// <summary>Sets the tenant subdomain (e.g., "community1").</summary>
    public PublicExperienceSettingsBuilder WithSubdomain(string subdomain)
    {
        _model = _model with { Subdomain = subdomain };
        return this;
    }

    /// <summary>Sets the tenant custom domain (e.g., "events.myorg.com").</summary>
    public PublicExperienceSettingsBuilder WithCustomDomain(string customDomain)
    {
        _model = _model with { CustomDomain = customDomain };
        return this;
    }

    // ── Home Page & Navigation ──────────────────────────────────────────

    /// <summary>Sets the preferred home page. "EventList" (default) or "LandingPage".</summary>
    public PublicExperienceSettingsBuilder WithHomePage(string preferredHomePage)
    {
        _model = _model with { PreferredHomePage = preferredHomePage };
        return this;
    }

    /// <summary>Configures event card click to open detail page.</summary>
    public PublicExperienceSettingsBuilder WithEventCardOpensDetail(bool enabled = true)
    {
        _model = _model with { EventCardClickOpensDetailPage = enabled };
        return this;
    }

    // ── Modules ─────────────────────────────────────────────────────────

    /// <summary>Enables the Islamic module.</summary>
    public PublicExperienceSettingsBuilder WithIslamicModule(bool enabled = true)
    {
        _model = _model with { IsIslamicModuleEnabled = enabled };
        if (enabled && !_model.EnabledModules.Contains("Islamic"))
            _model.EnabledModules.Add("Islamic");
        return this;
    }

    /// <summary>Enables the Tech module.</summary>
    public PublicExperienceSettingsBuilder WithTechModule(bool enabled = true)
    {
        _model = _model with { IsTechModuleEnabled = enabled };
        if (enabled && !_model.EnabledModules.Contains("Tech"))
            _model.EnabledModules.Add("Tech");
        return this;
    }

    // ── Content Policies ────────────────────────────────────────────────

    /// <summary>Controls whether users can submit events.</summary>
    public PublicExperienceSettingsBuilder WithUserSubmittedEvents(bool allowed = true)
    {
        _model = _model with { AllowUserSubmittedEvents = allowed };
        return this;
    }

    /// <summary>Controls whether organizations can submit events.</summary>
    public PublicExperienceSettingsBuilder WithOrganizationSubmittedEvents(bool allowed = true)
    {
        _model = _model with { AllowOrganizationSubmittedEvents = allowed };
        return this;
    }

    /// <summary>Controls whether groups can submit events.</summary>
    public PublicExperienceSettingsBuilder WithGroupSubmittedEvents(bool allowed = true)
    {
        _model = _model with { AllowGroupSubmittedEvents = allowed };
        return this;
    }

    /// <summary>Controls whether organizations can self-register.</summary>
    public PublicExperienceSettingsBuilder WithOrganizationSelfRegistration(bool allowed = true)
    {
        _model = _model with { AllowOrganizationSelfRegistration = allowed };
        return this;
    }

    /// <summary>Controls whether groups can self-register.</summary>
    public PublicExperienceSettingsBuilder WithGroupSelfRegistration(bool allowed = true)
    {
        _model = _model with { AllowGroupSelfRegistration = allowed };
        return this;
    }

    public PublicExperienceSettingsBuilder WithClientPickerEnabled(bool enabled)
    {
        _model = _model with { ClientPickerEnabled = enabled };
        return this;
    }

    /// <summary>Sets community guidelines content.</summary>
    public PublicExperienceSettingsBuilder WithCommunityGuidelines(string content)
    {
        _model = _model with { CommunityGuidelinesContent = content };
        return this;
    }

    // ── Analytics ────────────────────────────────────────────────────────

    /// <summary>
    /// Configures analytics with the specified provider.
    /// </summary>
    /// <param name="provider">Analytics provider name (e.g., "plausible", "umami", "none").</param>
    /// <param name="consentMode">Consent mode: "pseudonymous", "anonymous", "full".</param>
    /// <param name="apiKey">Optional public API key for the analytics provider.</param>
    /// <param name="endpointUrl">Optional endpoint URL for the analytics provider.</param>
    public PublicExperienceSettingsBuilder WithAnalytics(
        string provider,
        string consentMode = "pseudonymous",
        string? apiKey = null,
        string? endpointUrl = null)
    {
        _model = _model with { AnalyticsProvider = provider, AnalyticsEnabled = !provider.Equals("none", StringComparison.OrdinalIgnoreCase), AnalyticsConsentMode = consentMode };
        if (apiKey is not null)
            _model = _model with { AnalyticsPublicApiKey = apiKey };
        if (endpointUrl is not null)
            _model = _model with { AnalyticsEndpointUrl = endpointUrl };
        return this;
    }

    /// <summary>Sets analytics transport mode: "direct" or "proxy".</summary>
    public PublicExperienceSettingsBuilder WithAnalyticsTransport(string transportMode)
    {
        _model = _model with { AnalyticsTransportMode = transportMode };
        return this;
    }

    /// <summary>Enables or disables analytics user identification.</summary>
    public PublicExperienceSettingsBuilder WithAnalyticsIdentify(bool allowed = true)
    {
        _model = _model with { AnalyticsAllowIdentify = allowed };
        return this;
    }

    // ── Render Policy ───────────────────────────────────────────────────

    /// <summary>Sets the render policy preset and version.</summary>
    public PublicExperienceSettingsBuilder WithRenderPolicy(string preset, int version = 1)
    {
        _model = _model with { RenderPolicyPreset = preset, RenderPolicyVersion = version };
        return this;
    }

    /// <summary>Enables advanced render policy overrides.</summary>
    public PublicExperienceSettingsBuilder WithAdvancedRenderOverrides(bool enabled = true)
    {
        _model = _model with { EnableAdvancedRenderPolicyOverrides = enabled };
        return this;
    }

    /// <summary>Sets the global render mode and prerender flag.</summary>
    public PublicExperienceSettingsBuilder WithGlobalRenderMode(string mode, bool prerender = false)
    {
        _model = _model with { GlobalRenderMode = mode, GlobalPrerenderEnabled = prerender };
        return this;
    }

    // ── AI Assistant ────────────────────────────────────────────────────

    /// <summary>Sets AI assistant enabled flag and availability (configured state).</summary>
    public PublicExperienceSettingsBuilder WithAiAssistant(bool available = true)
    {
        _model = _model with { IsAiAssistantEnabled = true, IsAiAssistantAvailable = available };
        return this;
    }

    // ── Factory Methods ─────────────────────────────────────────────────

    /// <summary>
    /// Creates a settings model with default branding for ISLAMU Explore.
    /// Useful as a baseline for tests that need non-null settings.
    /// </summary>
    public static PublicExperienceSettingsBuilder DefaultBranded()
    {
        return new PublicExperienceSettingsBuilder()
            .WithBranding("ISLAMU Explore")
            .WithHomePage("EventList");
    }

    /// <summary>
    /// Creates a settings model configured for landing page as home.
    /// </summary>
    public static PublicExperienceSettingsBuilder WithLandingPage()
    {
        return new PublicExperienceSettingsBuilder()
            .WithBranding("ISLAMU Explore")
            .WithHomePage("LandingPage");
    }

    /// <summary>
    /// Creates a settings model with analytics enabled.
    /// </summary>
    public static PublicExperienceSettingsBuilder WithAnalyticsEnabled(string provider = "plausible")
    {
        return new PublicExperienceSettingsBuilder()
            .WithBranding("ISLAMU Explore")
            .WithAnalytics(provider, apiKey: "test-api-key", endpointUrl: "https://analytics.test");
    }

    // ── Build ───────────────────────────────────────────────────────────

    /// <summary>Builds the configured <see cref="PublicExperienceSettingsDto"/>.</summary>
    public PublicExperienceSettingsDto Build() => _model;

    /// <summary>Implicit conversion for concise usage in method parameters.</summary>
    public static implicit operator PublicExperienceSettingsDto(PublicExperienceSettingsBuilder builder)
        => builder.Build();
}
