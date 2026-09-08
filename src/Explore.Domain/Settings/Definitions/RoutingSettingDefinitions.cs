namespace Explore.Domain.Settings.Definitions;

public static class RoutingSettingDefinitions
{
    public static readonly SettingDefinition DefaultPublicHomePage = new(
        Key: "routing.default_public_home_page",
        ValueType: SettingValueType.String,
        DefaultValue: "\"EventList\"",
        Category: "Routing",
        Description: "Default public home page for tenants",
        MaxScope: SettingScope.Tenant,
        AllowedValues: ["EventList", "LandingPage"]);

    // Tenant resolution
    public static readonly SettingDefinition ResolverHeaderEnabled = new(
        Key: "routing.resolver_header_enabled",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "Routing",
        Description: "Enable tenant resolution via X-Tenant-ID header");

    public static readonly SettingDefinition ResolverSubdomainEnabled = new(
        Key: "routing.resolver_subdomain_enabled",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "Routing",
        Description: "Enable tenant resolution via subdomain");

    public static readonly SettingDefinition ResolverCustomDomainEnabled = new(
        Key: "routing.resolver_custom_domain_enabled",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Routing",
        Description: "Enable tenant resolution via custom domain mapping");

    public static readonly SettingDefinition ResolverPathEnabled = new(
        Key: "routing.resolver_path_enabled",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Routing",
        Description: "Enable tenant resolution via URL path prefix");

    public static readonly SettingDefinition PathPrefix = new(
        Key: "routing.path_prefix",
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "Routing",
        Description: "URL path prefix for path-based tenant resolution (e.g., /t/{tenant})");

    // Render Policy Tenant Override Controls
    public static readonly SettingDefinition RenderPolicyAllowTenantOverride = new(
        Key: "routing.render_policy.allow_tenant_override",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Routing",
        Description: "Whether tenants can override render policy settings");

    public static readonly SettingDefinition RenderPolicyLockTenantPublicSeo = new(
        Key: "routing.render_policy.lock_tenant_public_seo",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Routing",
        Description: "Lock tenant public SEO render policy to instance value");

    public static readonly SettingDefinition RenderPolicyLockTenantOperational = new(
        Key: "routing.render_policy.lock_tenant_operational",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Routing",
        Description: "Lock tenant operational render policy to instance value");

    public static readonly SettingDefinition RenderPolicyLockTenantAdmin = new(
        Key: "routing.render_policy.lock_tenant_admin",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Routing",
        Description: "Lock tenant admin render policy to instance value");

    // Render Policy
    public static readonly SettingDefinition RenderPolicyVersion = new(
        Key: "routing.render_policy.version",
        ValueType: SettingValueType.String,
        DefaultValue: "\"1\"",
        Category: "Routing",
        Description: "Render policy schema version");

    public static readonly SettingDefinition RenderPolicyPreset = new(
        Key: "routing.render_policy.preset",
        ValueType: SettingValueType.String,
        DefaultValue: "\"balanced\"",
        Category: "Routing",
        Description: "Render policy preset (balanced, performance, compatibility)");

    public static readonly SettingDefinition RenderPolicyAdvancedEnabled = new(
        Key: "routing.render_policy.advanced_enabled",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Routing",
        Description: "Whether advanced render policy configuration is enabled");

    public static readonly SettingDefinition RenderPolicyDisallowInteractiveServerOnOnboarding = new(
        Key: "routing.render_policy.onboarding.disallow_interactive_server",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "Routing",
        Description: "Disallow InteractiveServer render mode on onboarding pages");

    // Fallback (Global)
    public static readonly SettingDefinition FallbackRenderMode = new(
        Key: "routing.render_policy.global.render_mode",
        ValueType: SettingValueType.String,
        DefaultValue: "\"InteractiveAuto\"",
        Category: "Routing",
        Description: "Global fallback render mode");

    public static readonly SettingDefinition FallbackPrerenderEnabled = new(
        Key: "routing.render_policy.global.prerender_enabled",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "Routing",
        Description: "Global fallback prerender enabled");

    // PublicSeo
    public static readonly SettingDefinition PublicSeoRenderMode = new(
        Key: "routing.render_policy.public_seo.render_mode",
        ValueType: SettingValueType.String,
        DefaultValue: "\"InteractiveAuto\"",
        Category: "Routing",
        Description: "Public SEO pages render mode");

    public static readonly SettingDefinition PublicSeoPrerenderEnabled = new(
        Key: "routing.render_policy.public_seo.prerender_enabled",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "Routing",
        Description: "Public SEO pages prerender enabled");

    // Operational
    public static readonly SettingDefinition OperationalRenderMode = new(
        Key: "routing.render_policy.operational.render_mode",
        ValueType: SettingValueType.String,
        DefaultValue: "\"InteractiveAuto\"",
        Category: "Routing",
        Description: "Operational pages render mode");

    public static readonly SettingDefinition OperationalPrerenderEnabled = new(
        Key: "routing.render_policy.operational.prerender_enabled",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "Routing",
        Description: "Operational pages prerender enabled");

    // Admin
    public static readonly SettingDefinition AdminRenderMode = new(
        Key: "routing.render_policy.admin.render_mode",
        ValueType: SettingValueType.String,
        DefaultValue: "\"InteractiveAuto\"",
        Category: "Routing",
        Description: "Admin pages render mode");

    public static readonly SettingDefinition AdminPrerenderEnabled = new(
        Key: "routing.render_policy.admin.prerender_enabled",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "Routing",
        Description: "Admin pages prerender enabled");

    // Onboarding
    public static readonly SettingDefinition OnboardingRenderMode = new(
        Key: "routing.render_policy.onboarding.render_mode",
        ValueType: SettingValueType.String,
        DefaultValue: "\"InteractiveAuto\"",
        Category: "Routing",
        Description: "Onboarding pages render mode");

    public static readonly SettingDefinition OnboardingPrerenderEnabled = new(
        Key: "routing.render_policy.onboarding.prerender_enabled",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "Routing",
        Description: "Onboarding pages prerender enabled");

    public static IReadOnlyList<SettingDefinition> All =>
    [
        DefaultPublicHomePage,
        ResolverHeaderEnabled, ResolverSubdomainEnabled, ResolverCustomDomainEnabled,
        ResolverPathEnabled, PathPrefix,
        RenderPolicyAllowTenantOverride, RenderPolicyLockTenantPublicSeo,
        RenderPolicyLockTenantOperational, RenderPolicyLockTenantAdmin,
        RenderPolicyVersion, RenderPolicyPreset, RenderPolicyAdvancedEnabled,
        RenderPolicyDisallowInteractiveServerOnOnboarding,
        FallbackRenderMode, FallbackPrerenderEnabled,
        PublicSeoRenderMode, PublicSeoPrerenderEnabled,
        OperationalRenderMode, OperationalPrerenderEnabled,
        AdminRenderMode, AdminPrerenderEnabled,
        OnboardingRenderMode, OnboardingPrerenderEnabled
    ];
}
