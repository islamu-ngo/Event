using Explore.Domain.Constants;

namespace Event.Domain.UnitTests.Constants;

public class ReservedTenantSlugsTests
{
    [Test]
    public async Task IsReserved_WithBlazorRootSegments_ReturnsTrue()
    {
        string[] rootSegments =
        [
            "about", "admin", "ai", "auth", "community-guidelines", "contact", "error", "errors",
            "actors", "events", "event-created", "forbidden", "group", "home", "login", "logout",
            "my", "notifications", "onboarding", "organization", "organizations", "payments",
            "privacy", "registration", "settings", "setup", "startup", "status", "studio",
            "terms", "tickets", "users"
        ];

        await Assert.That(rootSegments.All(ReservedTenantSlugs.IsReserved)).IsTrue();
    }

    [Test]
    public async Task IsReserved_WithInfrastructureSegments_ReturnsTrue()
    {
        string[] infrastructureSegments =
            ["_framework", "_blazor", "api", "bff", "mcp", "oauth", "test-endpoint", "manifest.webmanifest"];

        await Assert.That(infrastructureSegments.All(ReservedTenantSlugs.IsReserved)).IsTrue();
    }

    [Test]
    public async Task IsReserved_WithShippedStaticAssetRoots_ReturnsTrue()
    {
        string[] staticAssetRoots =
        [
            "Explore.Blazor.styles.css", "Explore.Blazor.Client.bundle.scp.css",
            "push-service-worker.js", "image"
        ];

        await Assert.That(staticAssetRoots.All(ReservedTenantSlugs.IsReserved)).IsTrue();
    }

    [Test]
    public async Task IsReserved_WithBrandSafetyTerms_ReturnsTrue()
    {
        string[] brandSafetyTerms =
        [
            "root", "administrator", "security", "help", "support", "billing", "official", "event",
            "islamu", "system", "instance", "test"
        ];

        await Assert.That(brandSafetyTerms.All(ReservedTenantSlugs.IsReserved)).IsTrue();
    }

    [Test]
    public async Task IsReserved_WithValidTenantSlugs_ReturnsFalse()
    {
        string[] tenantSlugs = ["al-nour", "my-community", "masjid-al-iman"];

        await Assert.That(tenantSlugs.All(slug => !ReservedTenantSlugs.IsReserved(slug))).IsTrue();
    }

    [Test]
    public async Task IsReserved_WithDifferentCasing_ReturnsTrue()
    {
        await Assert.That(ReservedTenantSlugs.IsReserved("Admin")).IsTrue();
    }

    [Test]
    public async Task All_ContainsEverySpecifiedReservedSlug()
    {
        await Assert.That(ReservedTenantSlugs.All.Count).IsGreaterThanOrEqualTo(80);
    }
}
