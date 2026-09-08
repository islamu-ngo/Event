using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Providers;

namespace Explore.Blazor.Client.Services;

public sealed class RuntimeRenderPolicyService : IRuntimeRenderPolicyService
{
    private const string InteractiveAutoMode = "InteractiveAuto";
    private const string InteractiveWebAssemblyMode = "InteractiveWebAssembly";
    private const string InteractiveServerMode = "InteractiveServer";
    private const string SeoBalancedPreset = "SeoBalanced";

    private readonly IPublicExperienceService _publicExperienceService;
    private readonly ILogger<RuntimeRenderPolicyService> _logger;

    public RuntimeRenderPolicyService(
        IPublicExperienceService publicExperienceService,
        ILogger<RuntimeRenderPolicyService> logger)
    {
        _publicExperienceService = publicExperienceService;
        _logger = logger;
    }

    public async Task<RuntimeRenderPolicyDecision> ResolveForPathAsync(string? rawPath, CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizePath(rawPath);
        var routeGroup = ClassifyRouteGroup(normalizedPath);

        try
        {
            var settings = await _publicExperienceService.GetCachedSettingsAsync();
            return ResolveFromSettings(routeGroup, settings);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve runtime render policy for {Path}; using defaults.", normalizedPath);
            return BuildDefaultDecision(routeGroup);
        }
    }

    internal static RuntimeRenderPolicyDecision ResolveFromSettings(RuntimeRouteGroup routeGroup, PublicExperienceSettingsDto? settings)
    {
        if (settings is null)
        {
            return BuildDefaultDecision(routeGroup);
        }

        var renderMode = NormalizeRenderMode(settings.GlobalRenderMode);
        var prerenderEnabled = settings.GlobalPrerenderEnabled == true;

        if (settings.EnableAdvancedRenderPolicyOverrides == true)
        {
            switch (routeGroup)
            {
                case RuntimeRouteGroup.PublicSeo:
                    renderMode = NormalizeRenderMode(settings.PublicSeoRenderMode);
                    prerenderEnabled = settings.PublicSeoPrerenderEnabled == true;
                    break;
                case RuntimeRouteGroup.Admin:
                    renderMode = NormalizeRenderMode(settings.AdminRenderMode);
                    prerenderEnabled = settings.AdminPrerenderEnabled == true;
                    break;
                case RuntimeRouteGroup.Onboarding:
                    renderMode = NormalizeRenderMode(settings.OnboardingRenderMode);
                    prerenderEnabled = settings.OnboardingPrerenderEnabled == true;
                    break;
                default:
                    renderMode = NormalizeRenderMode(settings.OperationalRenderMode);
                    prerenderEnabled = settings.OperationalPrerenderEnabled == true;
                    break;
            }
        }
        else if (routeGroup == RuntimeRouteGroup.PublicSeo &&
                 settings.RenderPolicyPreset?.Equals(SeoBalancedPreset, StringComparison.OrdinalIgnoreCase) == true)
        {
            prerenderEnabled = true;
        }

        // Onboarding always uses InteractiveServer for instant interactivity
        // without WASM download delay — critical for first-impression conversion.
        if (routeGroup == RuntimeRouteGroup.Onboarding)
        {
            renderMode = InteractiveServerMode;
        }

        return new RuntimeRenderPolicyDecision(renderMode, prerenderEnabled, routeGroup);
    }

    public static RuntimeRouteGroup ClassifyRouteGroup(string normalizedPath)
    {
        if (normalizedPath.StartsWith("/onboarding/", StringComparison.Ordinal) ||
            normalizedPath.Equals("/setup", StringComparison.Ordinal) ||
            normalizedPath.Equals("/startup", StringComparison.Ordinal))
        {
            return RuntimeRouteGroup.Onboarding;
        }

        if (normalizedPath.StartsWith("/admin/", StringComparison.Ordinal))
        {
            return RuntimeRouteGroup.Admin;
        }

        if (normalizedPath.Equals("/", StringComparison.Ordinal) ||
            normalizedPath.Equals("/events", StringComparison.Ordinal) ||
            normalizedPath.Equals("/home", StringComparison.Ordinal) ||
            normalizedPath.StartsWith("/events/", StringComparison.Ordinal))
        {
            return RuntimeRouteGroup.PublicSeo;
        }

        return RuntimeRouteGroup.Operational;
    }

    public static string NormalizePath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return "/";
        }

        var path = rawPath.Trim();
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        if (path.Length > 1)
        {
            path = path.TrimEnd('/');
        }

        return path.ToLowerInvariant();
    }

    private static RuntimeRenderPolicyDecision BuildDefaultDecision(RuntimeRouteGroup routeGroup)
    {
        // Default to InteractiveServer for all route groups.
        // InteractiveServer connects via SignalR in <1s with no WASM download.
        // Tenant settings can override this to InteractiveAuto or WebAssembly at runtime.
        return routeGroup == RuntimeRouteGroup.PublicSeo
            ? new RuntimeRenderPolicyDecision(InteractiveServerMode, PrerenderEnabled: true, routeGroup)
            : new RuntimeRenderPolicyDecision(InteractiveServerMode, PrerenderEnabled: false, routeGroup);
    }

    private static string NormalizeRenderMode(string? renderMode)
    {
        if (string.IsNullOrWhiteSpace(renderMode))
        {
            return InteractiveServerMode;
        }

        if (renderMode.Equals(InteractiveWebAssemblyMode, StringComparison.OrdinalIgnoreCase))
        {
            return InteractiveWebAssemblyMode;
        }

        if (renderMode.Equals(InteractiveAutoMode, StringComparison.OrdinalIgnoreCase))
        {
            return InteractiveAutoMode;
        }

        return InteractiveServerMode;
    }
}

public sealed record RuntimeRenderPolicyDecision(string RenderMode, bool PrerenderEnabled, RuntimeRouteGroup RouteGroup);

public enum RuntimeRouteGroup
{
    PublicSeo,
    Operational,
    Admin,
    Onboarding
}
