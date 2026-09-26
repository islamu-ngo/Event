using System.Collections.Frozen;

namespace Explore.Domain.Constants;

public static class ReservedTenantSlugs
{
    public static readonly FrozenSet<string> All = new[]
    {
        "about", "admin", "ai", "auth", "community-guidelines", "contact", "error", "errors",
        "actors", "events", "event-created", "forbidden", "group", "home", "login", "logout",
        "my", "notifications", "onboarding", "organization", "organizations", "payments", "privacy",
        "registration", "settings", "setup", "startup", "status", "studio", "terms", "tickets",
        "users", "oauth", "test-endpoint",
        "_blazor", "_framework", "_content", "_host",
        "api", "bff", "health", "healthz", "metrics", "prometheus", "swagger", "openapi", "mcp",
        "connect", "signin-oidc", "signout-callback-oidc",
        "root", "administrator", "security", "help", "support", "billing", "official", "event",
        "islamu", "system", "instance", "test",
        "css", "js", "static", "assets", "fonts", "image", "images", "dist", "lib",
        "Explore.Blazor.styles.css", "Explore.Blazor.Client.bundle.scp.css", "favicon.ico",
        "robots.txt", "sitemap.xml", "manifest.json", "manifest.webmanifest", "service-worker.js",
        "push-service-worker.js"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsReserved(string slug) => All.Contains(slug);
}
