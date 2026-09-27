using Explore.Blazor.Client.Clients;
using Microsoft.Extensions.Caching.Memory;

namespace Explore.Blazor.Services;

public interface IBffResolverConfigurationProvider
{
    Task<ResolverConfigurationDto> GetConfigurationAsync(CancellationToken cancellationToken = default);
}

public sealed class BffResolverConfigurationProvider(
    IInstanceMessagingSettingsClient apiClient,
    IMemoryCache cache,
    ILogger<BffResolverConfigurationProvider> logger) : IBffResolverConfigurationProvider
{
    private const string CacheKey = "BffResolverConfiguration";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task<ResolverConfigurationDto> GetConfigurationAsync(
        CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue<ResolverConfigurationDto>(CacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var configuration = await apiClient.GetInstanceResolverConfigurationAsync(
                cancellationToken: cancellationToken);
            cache.Set(CacheKey, configuration, CacheDuration);
            return configuration;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Resolver configuration API read failed with {FailureType}; using the safe path-routing default.",
                ex.GetType().Name);
            return CreateFallback();
        }
    }

    private static ResolverConfigurationDto CreateFallback() => new()
    {
        HeaderEnabled = true,
        PathEnabled = true,
        PathPrefix = string.Empty,
        ReservedSlugs =
        [
            "about", "admin", "ai", "auth", "community-guidelines", "contact",
            "error", "errors", "actors", "events", "event-created", "forbidden", "group",
            "home", "login", "logout", "my", "notifications", "onboarding", "organization",
            "organizations", "payments", "privacy", "registration", "settings", "setup",
            "startup", "status", "studio", "terms", "tickets", "users", "oauth", "test-endpoint",
            "_blazor", "_framework", "_content", "_host",
            "api", "bff", "health", "healthz", "metrics", "prometheus", "swagger",
            "openapi", "mcp", "connect", "signin-oidc", "signout-callback-oidc",
            "root", "administrator", "security", "help", "support", "billing",
            "official", "event", "islamu", "system", "instance", "test",
            "css", "js", "static", "assets", "fonts", "image", "images", "dist", "lib",
            "Explore.Blazor.styles.css", "Explore.Blazor.Client.bundle.scp.css", "favicon.ico",
            "robots.txt", "sitemap.xml", "manifest.json", "manifest.webmanifest",
            "service-worker.js", "push-service-worker.js", "appsettings.json",
            "appsettings.Development.json"
        ],
        SubdomainEnabled = false,
        CustomDomainEnabled = false,
        InstanceBaseDomain = string.Empty,
        AllowTenantCustomDomains = false
    };
}
