using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Shell;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Services.Shell;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Components.Shell;

public sealed class TenantNavLinksRailTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly ITenantNavigationService _tenantNavigationService = Substitute.For<ITenantNavigationService>();

    public TenantNavLinksRailTests()
    {
        _ctx.Services.AddScoped<IWorkspaceRegistry, WorkspaceRegistry>();
        _ctx.Services.AddScoped<WorkspaceRouteClassifier>();
        _ctx.Services.AddScoped<UiShellState>();
        _ctx.Services.AddScoped<TenantNavLinksState>();
        _ctx.Services.AddSingleton(_tenantNavigationService);

        var shellContextService = Substitute.For<IUiShellContextService>();
        shellContextService.GetCachedContextAsync(Arg.Any<CancellationToken>())
            .Returns(new UiShellContextDto
            {
                Workspaces = new WorkspaceAvailabilityDto
                {
                    Events = true,
                    Studio = true,
                    Ai = true,
                    Settings = true
                }
            });
        _ctx.Services.AddSingleton(shellContextService);
        _ctx.SetAuthenticatedUser(Guid.CreateVersion7(), "Tenant User");
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task RenderTenantLinksShowsConfiguredIconHrefAndFaviconFallbackSource()
    {
        _tenantNavigationService.GetNavigationLinksAsync().Returns(
        [
            new TenantNavigationLinkDto
            {
                Id = Guid.CreateVersion7(),
                Label = "Documentation",
                Url = "https://docs.example.test/guide",
                Icon = Icons.Material.Filled.Info,
                Order = 1,
                OpenInNewTab = true
            },
            new TenantNavigationLinkDto
            {
                Id = Guid.CreateVersion7(),
                Label = "Community",
                Url = "https://community.example.test/welcome",
                Order = 2
            }
        ]);

        var cut = _ctx.Render<AppWorkspaceRail>();

        var links = cut.FindAll("[data-testid='tenant-rail-link']");
        await Assert.That(links.Count).IsEqualTo(2);
        await Assert.That(links[0].GetAttribute("href")).IsEqualTo("https://docs.example.test/guide");
        await Assert.That(links[0].GetAttribute("target")).IsEqualTo("_blank");
        await Assert.That(cut.FindComponents<TenantNavigationLinkIcon>()[0].Instance.Icon)
            .IsEqualTo(Icons.Material.Filled.Info);
        await Assert.That(cut.Find("img.tenant-navigation-link-icon__favicon").GetAttribute("src"))
            .IsEqualTo("https://community.example.test/favicon.ico");

        var faviconIcon = cut.FindComponents<TenantNavigationLinkIcon>()[1];
        await faviconIcon.Find("img").TriggerEventAsync("onerror", EventArgs.Empty);
        await Assert.That(faviconIcon.FindAll("img")).IsEmpty();
        await Assert.That(faviconIcon.FindComponent<MudIcon>().Instance.Icon)
            .IsEqualTo(Icons.Material.Filled.Link);
    }

    [Test]
    public async Task MobileLinksButtonOpensAndCloseButtonDismissesBottomSheet()
    {
        ConfigureSingleInternalLink();
        var cut = _ctx.Render<AppWorkspaceRail>();

        await cut.Find("[data-testid='mobile-links-tab']").ClickAsync(new MouseEventArgs());

        await Assert.That(cut.FindAll("[data-testid='tenant-nav-bottom-sheet']").Count).IsEqualTo(1);
        await Assert.That(cut.Find("[data-testid='mobile-links-tab']").GetAttribute("aria-expanded")).IsEqualTo("true");

        await cut.Find("[data-testid='tenant-nav-bottom-sheet-close']").ClickAsync(new MouseEventArgs());

        await Assert.That(cut.FindAll("[data-testid='tenant-nav-bottom-sheet']")).IsEmpty();
    }

    [Test]
    public async Task ClickingInternalSheetLinkNavigatesAndDismissesBottomSheet()
    {
        ConfigureSingleInternalLink();
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        var cut = _ctx.Render<AppWorkspaceRail>();
        await cut.Find("[data-testid='mobile-links-tab']").ClickAsync(new MouseEventArgs());

        await cut.Find("[data-testid='tenant-bottom-sheet-link']").ClickAsync(new MouseEventArgs());

        await Assert.That(new Uri(navigation.Uri).AbsolutePath).IsEqualTo("/community-guidelines");
        await Assert.That(cut.FindAll("[data-testid='tenant-nav-bottom-sheet']")).IsEmpty();
    }

    private void ConfigureSingleInternalLink()
    {
        _tenantNavigationService.GetNavigationLinksAsync().Returns(
        [
            new TenantNavigationLinkDto
            {
                Id = Guid.CreateVersion7(),
                Label = "Community Guidelines",
                Url = "/community-guidelines",
                Order = 1
            }
        ]);
    }
}
