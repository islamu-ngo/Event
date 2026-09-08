using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Shell;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Services.Shell;

namespace Explore.Blazor.Client.Tests.Components.Shell;

public sealed class AppWorkspaceRailTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IUiShellContextService _shellContextService = Substitute.For<IUiShellContextService>();

    public AppWorkspaceRailTests()
    {
        _ctx.Services.AddScoped<IWorkspaceRegistry, WorkspaceRegistry>();
        _ctx.Services.AddScoped<WorkspaceRouteClassifier>();
        _ctx.Services.AddScoped<UiShellState>();
        _ctx.Services.AddScoped<TenantNavLinksState>();
        var tenantNavigationService = Substitute.For<ITenantNavigationService>();
        tenantNavigationService.GetNavigationLinksAsync().Returns([]);
        _ctx.Services.AddSingleton(tenantNavigationService);
        _shellContextService.GetCachedContextAsync(Arg.Any<CancellationToken>())
            .Returns(new UiShellContextDto
            {
                Workspaces = new WorkspaceAvailabilityDto
                {
                    Events = true,
                    Studio = false,
                    Ai = true,
                    Settings = true
                }
            });
        _ctx.Services.AddSingleton(_shellContextService);
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task RenderHasApplicationWorkspacesNavLandmark()
    {
        _ctx.SetAnonymousUser();

        var cut = _ctx.Render<AppWorkspaceRail>();

        await Assert.That(cut.Find(".app-workspace-rail").GetAttribute("aria-label"))
            .IsEqualTo("Application workspaces");
    }

    [Test]
    public async Task RenderAnonymousUserShowsOnlyAnonymousWorkspaces()
    {
        _ctx.SetAnonymousUser();

        var cut = _ctx.Render<AppWorkspaceRail>();

        var links = cut.FindAll(".app-workspace-rail__link");
        await Assert.That(links.Count).IsEqualTo(1);
        await Assert.That(links[0].GetAttribute("href")).IsEqualTo("/");
    }

    [Test]
    public async Task RenderAuthenticatedUserShowsAllWorkspaces()
    {
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Test User");

        var cut = _ctx.Render<AppWorkspaceRail>();

        var links = cut.FindAll(".app-workspace-rail__link");
        await Assert.That(links.Count).IsEqualTo(3);
        await Assert.That(links.Any(link => link.GetAttribute("href") == "/ai")).IsTrue();
    }

    [Test]
    public async Task RenderSettingsIsPinnedToBlockEnd()
    {
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Test User");

        var cut = _ctx.Render<AppWorkspaceRail>();

        var links = cut.FindAll(".app-workspace-rail__link");
        await Assert.That(links[^1].GetAttribute("href")).IsEqualTo("/settings/personal");
    }

    [Test]
    public async Task RenderActiveWorkspaceGetsAriaCurrentPage()
    {
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Test User");
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/settings");

        var cut = _ctx.Render<AppWorkspaceRail>();

        var activeLink = cut.Find(".app-workspace-rail__link--active");
        await Assert.That(activeLink.GetAttribute("aria-current")).IsEqualTo("page");
        await Assert.That(activeLink.GetAttribute("href")).IsEqualTo("/settings/personal");
    }

    [Test]
    public async Task RenderContextualPersonalSettingsKeepsOneActiveWorkspaceAndMarksSettingsAsUtilityOpen()
    {
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Test User");
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        var shellState = _ctx.Services.GetRequiredService<UiShellState>();
        navigation.NavigateTo("/events?q=iftar");
        var cut = _ctx.Render<AppWorkspaceRail>();

        await cut.InvokeAsync(() => shellState.NavigateToPersonalSettings("/settings/personal/appearance"));

        cut.WaitForAssertion(() =>
        {
            var activeLinks = cut.FindAll(".app-workspace-rail__link--active");
            if (activeLinks.Count != 1)
            {
                throw new InvalidOperationException($"Expected one active link, found {activeLinks.Count}.");
            }

            if (activeLinks[0].GetAttribute("href") != "/events?q=iftar"
                || activeLinks[0].GetAttribute("aria-current") != "page")
            {
                throw new InvalidOperationException("Expected Events to remain the current workspace.");
            }

            var utilityLink = cut.Find(".app-workspace-rail__link--utility-open");
            if (utilityLink.GetAttribute("aria-current") is not null
                || utilityLink.GetAttribute("data-utility-open") != "true")
            {
                throw new InvalidOperationException("Expected Settings utility state without aria-current.");
            }
        });
    }

    [Test]
    public async Task RenderEventsRouteEventsLinkIsActive()
    {
        _ctx.SetAnonymousUser();
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/events");

        var cut = _ctx.Render<AppWorkspaceRail>();

        var activeLink = cut.Find(".app-workspace-rail__link--active");
        await Assert.That(activeLink.GetAttribute("href")).IsEqualTo("/events");
        await Assert.That(activeLink.GetAttribute("aria-current")).IsEqualTo("page");
    }

    [Test]
    public async Task RenderLinksRestoreLastWorkspaceRoutesWithQueries()
    {
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Test User");
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        _ = _ctx.Services.GetRequiredService<UiShellState>();
        navigation.NavigateTo("/events?q=iftar&format=online");
        navigation.NavigateTo("/settings/personal/appearance");

        var cut = _ctx.Render<AppWorkspaceRail>();
        var links = cut.FindAll(".app-workspace-rail__link");

        await Assert.That(links.Any(link => link.GetAttribute("href") == "/events?q=iftar&format=online")).IsTrue();
        await Assert.That(links.Any(link => link.GetAttribute("href") == "/settings/personal")).IsTrue();
    }

    [Test]
    public async Task RenderEachLinkHasVisibleLabelAndAriaLabel()
    {
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Test User");

        var cut = _ctx.Render<AppWorkspaceRail>();

        foreach (var link in cut.FindAll(".app-workspace-rail__link"))
        {
            await Assert.That(link.GetAttribute("aria-label")).IsNotNull();
            var label = link.QuerySelector(".app-workspace-rail__label");
            await Assert.That(label).IsNotNull();
            await Assert.That(label!.TextContent).IsNotNull().And.IsNotEmpty();
        }
    }
}
