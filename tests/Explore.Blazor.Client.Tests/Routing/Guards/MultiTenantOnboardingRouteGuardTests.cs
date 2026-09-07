using Blazouter.Models;
using Explore.Blazor.Client.Routing.Guards;

namespace Explore.Blazor.Client.Tests.Routing.Guards;

public class MultiTenantOnboardingRouteGuardTests
{
    [Test]
    public async Task CanActivateAsync_SingleTenantWhileOnboardingRequired_ReturnsTrue()
    {
        var instanceOnboardingService = Substitute.For<IInstanceOnboardingService>();
        instanceOnboardingService.GetSystemOnboardingStatusAsync().Returns(new SystemOnboardingStatusDto
        {
            RequiresOnboarding = true,
            DeploymentMode = "SingleTenant"
        });

        var guard = new MultiTenantOnboardingRouteGuard(instanceOnboardingService);

        var result = await guard.CanActivateAsync(new RouteMatch { MatchedPath = "/onboarding/instance" });

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

        var guard = new MultiTenantOnboardingRouteGuard(instanceOnboardingService);

        var result = await guard.CanActivateAsync(new RouteMatch { MatchedPath = "/onboarding/instance" });

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CanActivateAsync_MultiTenant_ReturnsTrue()
    {
        var instanceOnboardingService = Substitute.For<IInstanceOnboardingService>();
        instanceOnboardingService.GetSystemOnboardingStatusAsync().Returns(new SystemOnboardingStatusDto
        {
            RequiresOnboarding = false,
            DeploymentMode = "MultiTenant"
        });

        var guard = new MultiTenantOnboardingRouteGuard(instanceOnboardingService);

        var result = await guard.CanActivateAsync(new RouteMatch { MatchedPath = "/onboarding/tenant" });

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task GetRedirectPathAsync_ReturnsStartup()
    {
        var instanceOnboardingService = Substitute.For<IInstanceOnboardingService>();
        var guard = new MultiTenantOnboardingRouteGuard(instanceOnboardingService);

        var redirectPath = await guard.GetRedirectPathAsync(new RouteMatch { MatchedPath = "/onboarding/tenant" });

        await Assert.That(redirectPath).IsEqualTo("/startup");
    }
}
