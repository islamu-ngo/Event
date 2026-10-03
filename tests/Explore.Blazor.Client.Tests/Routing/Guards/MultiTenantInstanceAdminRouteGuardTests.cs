using Blazouter.Models;
using Explore.Blazor.Client.Routing.Guards;

namespace Explore.Blazor.Client.Tests.Routing.Guards;

public class MultiTenantInstanceAdminRouteGuardTests
{
    [Test]
    public async Task CanActivateAsync_MultiTenant_ReturnsTrue()
    {
        var instanceOnboardingService = Substitute.For<IInstanceOnboardingService>();
        instanceOnboardingService.GetSystemOnboardingStatusAsync().Returns(new SystemOnboardingStatusDto
        {
            RequiresOnboarding = false,
            DeploymentMode = "MultiTenant"
        });

        var guard = new MultiTenantInstanceAdminRouteGuard(instanceOnboardingService);

        var result = await guard.CanActivateAsync(new RouteMatch { MatchedPath = "/admin/instance" });

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task CanActivateAsync_CompletedSingleTenant_ReturnsFalse()
    {
        var instanceOnboardingService = Substitute.For<IInstanceOnboardingService>();
        instanceOnboardingService.GetSystemOnboardingStatusAsync().Returns(new SystemOnboardingStatusDto
        {
            RequiresOnboarding = false,
            DeploymentMode = "SingleTenant"
        });

        var guard = new MultiTenantInstanceAdminRouteGuard(instanceOnboardingService);

        var result = await guard.CanActivateAsync(new RouteMatch { MatchedPath = "/admin/instance" });

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CanActivateAsync_SingleTenantWhileOnboardingRequired_ReturnsFalse()
    {
        var instanceOnboardingService = Substitute.For<IInstanceOnboardingService>();
        instanceOnboardingService.GetSystemOnboardingStatusAsync().Returns(new SystemOnboardingStatusDto
        {
            RequiresOnboarding = true,
            DeploymentMode = "SingleTenant"
        });

        var guard = new MultiTenantInstanceAdminRouteGuard(instanceOnboardingService);

        var result = await guard.CanActivateAsync(new RouteMatch { MatchedPath = "/admin/instance" });

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CanActivateAsync_StatusUnavailable_ReturnsFalse()
    {
        var instanceOnboardingService = Substitute.For<IInstanceOnboardingService>();
        instanceOnboardingService.GetSystemOnboardingStatusAsync().Returns((SystemOnboardingStatusDto?)null);

        var guard = new MultiTenantInstanceAdminRouteGuard(instanceOnboardingService);

        var result = await guard.CanActivateAsync(new RouteMatch { MatchedPath = "/admin/instance" });

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task GetRedirectPathAsync_ReturnsTenantSettings()
    {
        var instanceOnboardingService = Substitute.For<IInstanceOnboardingService>();
        var guard = new MultiTenantInstanceAdminRouteGuard(instanceOnboardingService);

        var redirectPath = await guard.GetRedirectPathAsync(new RouteMatch { MatchedPath = "/admin/instance" });

        await Assert.That(redirectPath).IsEqualTo("/settings/admin");
    }
}
