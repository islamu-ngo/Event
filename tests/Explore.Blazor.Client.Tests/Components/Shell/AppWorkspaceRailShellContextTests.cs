using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Shell;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Services.Shell;
using NSubstitute;

namespace Explore.Blazor.Client.Tests.Components.Shell;

public sealed class AppWorkspaceRailShellContextTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    public AppWorkspaceRailShellContextTests()
    {
        _ctx.Services.AddScoped<IWorkspaceRegistry, WorkspaceRegistry>();
        _ctx.Services.AddScoped<WorkspaceRouteClassifier>();
        _ctx.Services.AddScoped<UiShellState>();
        _ctx.Services.AddScoped<TenantNavLinksState>();
        var tenantNavigationService = Substitute.For<ITenantNavigationService>();
        tenantNavigationService.GetNavigationLinksAsync().Returns([]);
        _ctx.Services.AddSingleton(tenantNavigationService);
        _ctx.Services.AddScoped<IUiShellContextService, UiShellContextService>();
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task RenderAnonymousUser_DoesNotCallShellContextEndpoint()
    {
        _ctx.SetAnonymousUser();
        var apiClient = _ctx.Services.GetRequiredService<IUiShellClient>();

        _ctx.Render<AppWorkspaceRail>();

        await apiClient.DidNotReceive().GetUiShellContextAsync(
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RenderAuthenticatedUser_StudioHidden_WhenContextReportsStudioUnavailable()
    {
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Test User");
        SetupShellContext(studioAvailable: false);

        var cut = _ctx.Render<AppWorkspaceRail>();

        var links = cut.FindAll(".app-workspace-rail__link");
        var hrefs = links.Select(l => l.GetAttribute("href")).ToList();
        await Assert.That(hrefs).DoesNotContain("/studio");
    }

    [Test]
    public async Task RenderAuthenticatedUser_StudioVisible_WhenContextReportsStudioAvailable()
    {
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Test User");
        SetupShellContext(studioAvailable: true);

        var cut = _ctx.Render<AppWorkspaceRail>();

        var links = cut.FindAll(".app-workspace-rail__link");
        var hrefs = links.Select(l => l.GetAttribute("href")).ToList();
        await Assert.That(hrefs).Contains("/studio");
    }

    [Test]
    public async Task RenderSettingsRemainsAtBlockEnd_WhenStudioVisible()
    {
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Test User");
        SetupShellContext(studioAvailable: true);

        var cut = _ctx.Render<AppWorkspaceRail>();

        var links = cut.FindAll(".app-workspace-rail__link");
        await Assert.That(links[^1].GetAttribute("href")).IsEqualTo("/settings/personal");
    }

    [Test]
    public async Task RenderAuthorizedSettingsScopes_ShowsOneGearAndNativeScopeMenu()
    {
        var organizationId = Guid.NewGuid();
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Settings Admin");
        SetupShellContext(
            studioAvailable: false,
            settingsScopes:
            [
                new SettingsScopeDto { Scope = "Organization", ScopeId = organizationId, DisplayName = "Community" },
                new SettingsScopeDto { Scope = "Tenant", DisplayName = "Tenant" }
            ]);

        var cut = _ctx.Render<AppWorkspaceRail>();

        await Assert.That(cut.FindAll("a[href='/settings/personal']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll(".app-workspace-rail__settings-menu").Count).IsEqualTo(1);
        await Assert.That(cut.Find(".app-workspace-rail__settings-menu summary").GetAttribute("aria-label"))
            .IsEqualTo("Open Settings menu");
        await Assert.That(cut.Markup).Contains("href=\"/settings\"");
        await Assert.That(cut.Markup).Contains($"href=\"/settings/organization/{organizationId}\"");
        await Assert.That(cut.Markup).Contains("href=\"/settings/admin\"");
        await Assert.That(cut.Markup).DoesNotContain("/settings/instance");
    }

    private void SetupShellContext(
        bool studioAvailable,
        IReadOnlyList<SettingsScopeDto>? settingsScopes = null,
        string deploymentMode = "MultiTenant")
    {
        var shellContextService = Substitute.For<IUiShellContextService>();
        shellContextService.GetCachedContextAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<UiShellContextDto?>(new UiShellContextDto
            {
                DeploymentMode = deploymentMode,
                SettingsScopes = settingsScopes?.ToList() ?? [],
                Workspaces = new WorkspaceAvailabilityDto
                {
                    Events = true,
                    Settings = true,
                    Studio = studioAvailable
                }
            }));
        _ctx.Services.AddSingleton(shellContextService);
    }
}
