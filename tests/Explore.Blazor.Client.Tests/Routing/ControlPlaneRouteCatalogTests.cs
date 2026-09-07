using Explore.Blazor.Client.Routing.ControlPlane;

namespace Explore.Blazor.Client.Tests.Routing;

public class ControlPlaneRouteCatalogTests
{
    [Test]
    public async Task Navigation_ShouldExposeOnlyTopLevelInstanceRoutes()
    {
        var catalog = new ControlPlaneRouteCatalog();

        var navigationPaths = string.Join('\n', catalog.Navigation.Select(route => route.Path));

        await Assert.That(navigationPaths).Contains(ControlPlaneRoutes.Overview);
        await Assert.That(navigationPaths).Contains(ControlPlaneRoutes.Tenants);
        await Assert.That(navigationPaths).Contains(ControlPlaneRoutes.Domains);
        await Assert.That(navigationPaths).Contains(ControlPlaneRoutes.Operations);
        await Assert.That(navigationPaths).Contains(ControlPlaneRoutes.Plans);
        await Assert.That(navigationPaths).DoesNotContain(ControlPlaneRoutes.PlanDetail);
        await Assert.That(navigationPaths).DoesNotContain(ControlPlaneRoutes.TenantConfiguration);
        await Assert.That(navigationPaths).DoesNotContain("{");
    }

    [Test]
    public async Task TenantNavigation_ShouldExposeTenantRouteTemplatesOnly()
    {
        var catalog = new ControlPlaneRouteCatalog();

        var tenantNavigationPaths = string.Join('\n', catalog.TenantNavigation.Select(route => route.Path));

        await Assert.That(tenantNavigationPaths).Contains(ControlPlaneRoutes.TenantRoot);
        await Assert.That(tenantNavigationPaths).Contains(ControlPlaneRoutes.TenantSettings);
        await Assert.That(tenantNavigationPaths).Contains(ControlPlaneRoutes.TenantBranding);
        await Assert.That(tenantNavigationPaths).Contains(ControlPlaneRoutes.TenantModeration);
        await Assert.That(tenantNavigationPaths).Contains(ControlPlaneRoutes.TenantUsers);
        await Assert.That(tenantNavigationPaths).Contains(ControlPlaneRoutes.TenantFooterNavigation);
        await Assert.That(tenantNavigationPaths).Contains(ControlPlaneRoutes.TenantReports);
        await Assert.That(tenantNavigationPaths).Contains(ControlPlaneRoutes.TenantEvents);
        await Assert.That(tenantNavigationPaths).Contains(ControlPlaneRoutes.TenantPolicies);
        await Assert.That(tenantNavigationPaths).Contains("/tenant/{TenantSlug}/");
        await Assert.That(tenantNavigationPaths).DoesNotContain(ControlPlaneRoutes.Overview);
        await Assert.That(tenantNavigationPaths).DoesNotContain(ControlPlaneRoutes.PlanDetail);
    }
}
