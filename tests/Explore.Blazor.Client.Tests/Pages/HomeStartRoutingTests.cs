using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Pages;
using Explore.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;

namespace Explore.Blazor.Client.Tests.Pages;

public class HomeStartRoutingTests : IDisposable
{
    private readonly BlazorTestContext _context = new();

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    [Test]
    [Arguments("https://event.test/", "/communities", "https://event.test/communities/acme/")]
    [Arguments("https://event.test/community/", "/communities", "https://event.test/community/communities/acme/")]
    [Arguments("https://event.test/community/", "", "https://event.test/community/acme/")]
    [Arguments("https://event.test/community/", null, "https://event.test/community/acme/")]
    public async Task AuthenticatedTenant_RedirectsWithinConfiguredRoute(
        string baseUri,
        string? pathPrefix,
        string expectedUri)
    {
        var navigation = new RecordingNavigationManager(baseUri);
        _context.Services.AddSingleton<NavigationManager>(navigation);

        var onboarding = Substitute.For<IInstanceOnboardingService>();
        onboarding.GetStartupStatusAsync(Arg.Any<CancellationToken>())
            .Returns(new InstanceOnboardingStartupStatus(
                InstanceOnboardingStartupDisposition.Completed,
                Provider: null,
                Generation: 1,
                IsAuthenticated: true,
                IsCurrentUserInstanceAdmin: false,
                SelectedDeploymentMode: "MultiTenant"));
        onboarding.GetResolverConfigurationAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ResolverConfigurationDto?>(
                pathPrefix is null ? null : new ResolverConfigurationDto
                {
                    PathPrefix = pathPrefix,
                    PathEnabled = true
                }));
        _context.Services.AddSingleton(onboarding);

        var user = Substitute.For<IUserService>();
        user.ResolveUserTenantRedirectionAsync()
            .Returns(new UserTenantRedirectionDto { TenantSlug = "acme" });
        _context.Services.AddSingleton(user);
        var shellContext = Substitute.For<IUiShellContextService>();
        shellContext.GetCachedContextAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<UiShellContextDto?>(null));
        _context.Services.AddSingleton(shellContext);
        _context.Services.AddSingleton(Substitute.For<IStartupRoutingService>());

        _context.RenderMudComponent<HomeStart>();

        await Assert.That(navigation.Uri).IsEqualTo(expectedUri);
        await Assert.That(navigation.ForceLoad).IsTrue();
    }

    private sealed class RecordingNavigationManager : NavigationManager
    {
        public RecordingNavigationManager(string baseUri) => Initialize(baseUri, baseUri);

        public bool ForceLoad { get; private set; }

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            Uri = ToAbsoluteUri(uri).AbsoluteUri;
            ForceLoad = options.ForceLoad;
        }
    }
}
