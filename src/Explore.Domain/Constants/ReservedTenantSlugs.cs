using System.Collections.Frozen;

namespace Explore.Domain.Constants;

public static class ReservedTenantSlugs
{
    public static readonly FrozenSet<string> All = new[]
    {
        "about", "admin", "ai", "auth", "community-guidelines", "contact", "error", "errors",
        "actors", "events", "event-created", "forbidden", "group", "home", "login", "logout",
        "moderation", "my", "notifications", "onboarding", "organization", "organizations", "payments", "privacy",
        "registration", "settings", "setup", "startup", "status", "studio", "terms", "tickets",
        "users", "oauth", "test-endpoint",
        "_blazor", "_framework", "_content", "_host",
        "api", "bff", "alive", "health", "healthz", "metrics", "prometheus", "swagger", "openapi", "mcp",
        "vapid-public-key", "scalar", "quartz",
        "connect", "signin-atproto", "signin-oidc", "signout-callback-oidc",
        "root", "administrator", "security", "help", "support", "billing", "official", "event",
        "islamu", "system", "instance", "test",
        "css", "js", "static", "assets", "fonts", "image", "images", "dist", "lib",
        "Explore.Blazor.styles.css", "Explore.Blazor.Client.bundle.scp.css", "favicon.ico",
        "robots.txt", "sitemap.xml", "manifest.json", "manifest.webmanifest", "service-worker.js",
        "push-service-worker.js", "appsettings.json", "appsettings.Development.json"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    // Dotted asset/metadata filenames, including build fingerprints, cannot be tenant roots.
    public static bool IsReserved(string slug) => All.Contains(slug) || slug.Contains('.');
}
