using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Pages;
using Explore.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;

namespace Explore.Blazor.Client.Tests.Pages;

public sealed class HomeStartTests
{
    [Test]
    public async Task AuthenticatedTenantRootWithoutCascadeUsesShellTenantBeforeRouting()
    {
        using var context = new BlazorTestContext();
        var onboarding = Substitute.For<IInstanceOnboardingService>();
        onboarding.GetStartupStatusAsync(Arg.Any<CancellationToken>()).Returns(
            new InstanceOnboardingStartupStatus(
                InstanceOnboardingStartupDisposition.Completed,
                Provider: "Local",
                Generation: 1,
                IsAuthenticated: true,
                IsCurrentUserInstanceAdmin: false,
                SelectedDeploymentMode: "MultiTenant"));
        context.Services.AddSingleton(onboarding);
        context.Services.AddSingleton(Substitute.For<IUserService>());

        var routing = Substitute.For<IStartupRoutingService>();
        routing.GetRootDecisionAsync().Returns(StartupRouteDecision.Unavailable);
        context.Services.AddSingleton(routing);

        var shellContext = Substitute.For<IUiShellContextService>();
        shellContext.GetCachedContextAsync(Arg.Any<CancellationToken>()).Returns(
            new UiShellContextDto
            {
                TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001")
            });
        context.Services.AddSingleton(shellContext);

        context.RenderMudComponent<HomeStart>();

        await Assert.That(context.Services.GetRequiredService<NavigationManager>().Uri)
            .EndsWith("/startup");
    }
}
