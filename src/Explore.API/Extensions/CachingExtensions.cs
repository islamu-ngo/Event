using Explore.Application.Constants;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.API.Extensions;

public static class CachingExtensions
{
    public static IServiceCollection AddApiCaching(this IServiceCollection services)
    {
        services.AddOutputCache(options =>
        {
            options.AddBasePolicy(builder => builder.NoCache());

            // PublicData: truly public lookup endpoints (categories, tags, languages) — no auth variance needed
            options.AddPolicy("PublicData", builder => builder
                .Expire(TimeSpan.FromHours(1))
                .SetVaryByHeader(TenantHeaderNames.TenantSlug, "Host")
                .Tag("lookup-data"));

            // LookupData: kept for backward compatibility, same as PublicData
            options.AddPolicy("LookupData", builder => builder
                .Expire(TimeSpan.FromHours(1))
                .SetVaryByHeader(TenantHeaderNames.TenantSlug, "Host")
                .Tag("lookup-data"));

            // ListData: varies by Authorization for auth-aware HATEOAS links
            options.AddPolicy("ListData", builder => builder
                .Expire(TimeSpan.FromSeconds(30))
                .SetVaryByHeader(TenantHeaderNames.TenantSlug, "Host", "Authorization")
                .SetVaryByQuery("*")
                .Tag("list-data"));

            options.AddPolicy("EventDiscovery", builder => builder
                .Expire(TimeSpan.FromSeconds(30))
                .SetVaryByHeader(
                    TenantHeaderNames.TenantSlug,
                    "Host",
                    "Authorization",
                    "Prefer")
                .SetVaryByQuery("*")
                .Tag("list-data")
                .Tag("event-discovery"));

            // DetailData: varies by Authorization for auth-aware HATEOAS links
            options.AddPolicy("DetailData", builder => builder
                .Expire(TimeSpan.FromSeconds(60))
                .SetVaryByHeader(TenantHeaderNames.TenantSlug, "Host", "Authorization")
                .SetVaryByRouteValue("id")
                .Tag("detail-data"));

            // TenantNav: tenant navigation links — short expiry, evicted on write by "tenant-nav" tag
            options.AddPolicy("TenantNav", builder => builder
                .Expire(TimeSpan.FromMinutes(5))
                .SetVaryByHeader(TenantHeaderNames.TenantSlug, "Host")
                .Tag("tenant-nav"));

            // PublicExperienceShell: anonymous tenant shell, body ETagged by middleware and short-lived between setting edits.
            options.AddPolicy("PublicExperienceShell", builder => builder
                .Expire(TimeSpan.FromSeconds(30))
                .SetVaryByHeader(TenantHeaderNames.TenantSlug, "Host")
                .Tag("public-experience-shell"));

            options.AddPolicy("PublicHomeDiscovery", builder => builder
                .Expire(TimeSpan.FromSeconds(30))
                .SetVaryByHeader(TenantHeaderNames.TenantSlug, "Host")
                .SetVaryByQuery("areaId", "mode")
                .Tag("public-home-discovery"));

            options.AddPolicy("PublicLegalDocuments", builder => builder
                .Expire(TimeSpan.FromSeconds(30))
                .SetVaryByHeader(
                    TenantHeaderNames.TenantSlug,
                    "Host",
                    "Accept-Language")
                .Tag("public-legal-documents"));

            // SystemConfig: public first-run/runtime mode state for BFF startup.
            options.AddPolicy("SystemConfig", builder => builder
                .Expire(TimeSpan.FromSeconds(10))
                .SetVaryByHeader("Host")
                .Tag("system-config"));

            // SitemapData: public SEO sitemap — tenant/host aware, no auth variance.
            options.AddPolicy("SitemapData", builder => builder
                .Expire(TimeSpan.FromMinutes(30))
                .SetVaryByHeader(TenantHeaderNames.TenantSlug, "Host")
                .Tag("seo-sitemap"));
        });

        services.AddHybridCache(options =>
        {
            options.MaximumPayloadBytes = 1024 * 1024 * 10; // 10MB
            options.MaximumKeyLength = 512;
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(30),
                LocalCacheExpiration = TimeSpan.FromMinutes(5)
            };
        });

        return services;
    }
}
