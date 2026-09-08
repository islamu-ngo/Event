using Explore.Blazor.Client.Components.Shell;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.Ai;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Contracts.Services.SupportAccess;
using Explore.Blazor.Client.Layout;
using Explore.Blazor.Client.Services.Ai;
using Explore.Blazor.Client.Services.Docking;
using Explore.Blazor.Client.Services.Shell;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Layout;

/// <summary>
/// Behavioral tests for MainLayout covering:
/// - WCAG 2.4.1 accessibility landmarks (skip link, ARIA live regions, main content landmark)
/// - Chrome visibility toggling on setup/onboarding/startup routes
/// - User sync on first authenticated render
/// - Settings-driven sidebar content (brand name, community guidelines)
/// - Theme initialization
/// </summary>
public class MainLayoutTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private readonly IUserService _userService;
    private readonly IPublicExperienceService _publicExperienceService;
    private readonly IAppearanceThemeService _appearanceThemeService;
    private readonly IDockLayoutPersistence _dockLayoutPersistence;
    private readonly IShellPreferencesService _shellPreferencesService;

    public MainLayoutTests()
    {
        _ctx = new BlazorTestContext();

        // Explicit state registration for assertion control (not via AddShellStateMocks)
        _ctx.Services.AddScoped<AiAssistantState>();
        _ctx.Services.AddScoped<AiAssistantConversationState>();
        _ctx.Services.AddScoped<MainContentAppearanceState>();
        _ctx.Services.AddScoped<TenantNavLinksState>();
        _ctx.Services.AddScoped<DockLayoutState>();
        _ctx.Services.AddScoped<IDockPanelRegistry>(provider => provider.GetRequiredService<DockLayoutState>());
        _ctx.Services.AddScoped<IWorkspaceRegistry, WorkspaceRegistry>();
        _ctx.Services.AddScoped<WorkspaceRouteClassifier>();
        _ctx.Services.AddScoped<UiShellState>();
        var aiClientService = Substitute.For<IAiAssistantClientService>();
        aiClientService.GetConversationCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalCollectionResourceOfAiConversationSummaryDto?>(new HalCollectionResourceOfAiConversationSummaryDto
            {
                _embedded = new HalCollectionEmbeddedOfAiConversationSummaryDto { Items = [] }
            }));
        _ctx.Services.AddSingleton(aiClientService);

        _dockLayoutPersistence = Substitute.For<IDockLayoutPersistence>();
        _dockLayoutPersistence.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DockLayoutSnapshot?>(null));
        _dockLayoutPersistence.SaveAsync(Arg.Any<DockLayoutSnapshot>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        _dockLayoutPersistence.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        _ctx.Services.AddSingleton(_dockLayoutPersistence);

        NavMenuTestServices.Register(_ctx);
        _shellPreferencesService = Substitute.For<IShellPreferencesService>();
        _shellPreferencesService.LoadAsync(Arg.Any<UiShellContextDto>(), Arg.Any<CancellationToken>())
            .Returns(new ShellPreferenceState(WorkspaceKey.Events.Value, null, "/settings/personal"));
        _ctx.Services.AddSingleton(_shellPreferencesService);

        // Override IUserService for SyncUserAsync assertions (last AddSingleton wins)
        _userService = Substitute.For<IUserService>();
        _ctx.Services.AddSingleton(_userService);

        // Override IPublicExperienceService for settings assertions
        _publicExperienceService = Substitute.For<IPublicExperienceService>();
        _publicExperienceService.ResolveHomeRoute(Arg.Any<PublicExperienceSettingsDto?>()).Returns("/events");
        _ctx.Services.AddSingleton(_publicExperienceService);

        // Theme service — CreateTheme returns a valid MudTheme to avoid NRE
        _appearanceThemeService = Substitute.For<IAppearanceThemeService>();
        _appearanceThemeService.Current.Returns(new AppearanceState());
        _appearanceThemeService.CreateTheme(Arg.Any<string>()).Returns(new MudTheme());
        _appearanceThemeService.InitializeAsync(Arg.Any<MudThemeProvider>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _appearanceThemeService.ResolveEffectiveDarkModeAsync(Arg.Any<MudThemeProvider>())
            .Returns(Task.FromResult(false));
        _appearanceThemeService.SetThemeModeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _ctx.Services.AddSingleton(_appearanceThemeService);

        var supportAccessClientService = Substitute.For<ISupportAccessClientService>();
        supportAccessClientService.RefreshAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        supportAccessClientService.StopCurrentAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SupportAccessCommandResult.Succeeded()));
        _ctx.Services.AddSingleton(supportAccessClientService);

        // AnalyticsInitializer child deps (GetSettingsAsync returns null → early-return, no JS calls)
        _ctx.Services.AddSingleton(Substitute.For<IAnalyticsInterop>());
        _ctx.Services.AddSingleton(Substitute.For<ICookieConsentInterop>());
        _ctx.Services.AddSingleton(new CookieConsentStateService());
    }

    private IRenderedComponent<MainLayout> RenderLayout()
    {
        return _ctx.Render<MainLayout>(p =>
            p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "Test body content"))));
    }

    private static DockLayoutSnapshot CreateShellSnapshot(bool workspaceNavOpen, bool aiAssistantOpen)
    {
        return new DockLayoutSnapshot(
            "shell",
            [
                new DockPanelState(ShellDockPanels.WorkspaceNavId, workspaceNavOpen, DockMode.Docked, Width: 320, Order: 10, IsActive: workspaceNavOpen),
                new DockPanelState(ShellDockPanels.AiAssistantId, aiAssistantOpen, DockMode.Docked, Width: 420, Order: 20, IsActive: aiAssistantOpen)
            ],
            TestTime.UtcNow);
    }

    private static DockPanelDescriptor CreateWorkspacePersistentDescriptor(DockPanelId id)
    {
        return new DockPanelDescriptor(
            id,
            DockScope.Workspace,
            DockSide.End,
            DockMode.Docked,
            Title: "Workspace panel",
            AriaLabel: "Workspace panel",
            DefaultWidth: 320,
            MinWidth: 280,
            MaxWidth: 520,
            Order: 10,
            IsResizable: true,
            CanClose: true,
            PersistState: true);
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task MobileViewportClosesOpenWorkspaceNavigation()
    {
        var cut = RenderLayout();
        DockLayoutState dockLayout = _ctx.Services
            .GetRequiredService<DockLayoutState>();
        await Assert.That(dockLayout.GetPanel(
            ShellDockPanels.WorkspaceNavId)?.State.IsOpen).IsTrue();

        dockLayout.UpdateViewport(
            viewportWidth: 390,
            isMobile: true,
            DockScope.Shell,
            minimumContentWidth: 375);

        await Assert.That(dockLayout.GetPanel(
            ShellDockPanels.WorkspaceNavId)?.State.IsOpen).IsFalse();
        await Assert.That(cut.Markup).DoesNotContain(
            "workspace-navigation-panel--open");
    }

    [Test]
    public async Task LoginRouteDoesNotOpenWorkspaceNavigationDuringPrerender()
    {
        _ctx.Services.GetRequiredService<NavigationManager>()
            .NavigateTo("/login");

        var cut = RenderLayout();
        DockLayoutState dockLayout = _ctx.Services
            .GetRequiredService<DockLayoutState>();

        await Assert.That(dockLayout.GetPanel(
            ShellDockPanels.WorkspaceNavId)?.State.IsOpen).IsFalse();
        await Assert.That(cut.Markup).DoesNotContain(
            "workspace-navigation-panel--open");
    }

    #region Accessibility

    [Test]
    public async Task Render_HasSkipToContentLink_ForKeyboardNavigation()
    {
        var cut = RenderLayout();

        var skipLink = cut.Find("a.skip-link");

        await Assert.That(skipLink.GetAttribute("href")).IsEqualTo("#main-content");
        await Assert.That(skipLink.TextContent).Contains("Skip to main content");
    }

    [Test]
    public async Task Render_HasMainContentLandmark_WithNegativeTabIndex()
    {
        var cut = RenderLayout();

        var main = cut.Find("main#main-content");

        await Assert.That(main.GetAttribute("tabindex")).IsEqualTo("-1");
    }

    [Test]
    public async Task Render_HasAriaLiveRegions_ForDynamicAnnouncements()
    {
        var cut = RenderLayout();

        var polite = cut.Find("#aria-live-polite");
        await Assert.That(polite.GetAttribute("aria-live")).IsEqualTo("polite");
        await Assert.That(polite.GetAttribute("aria-atomic")).IsEqualTo("true");

        var assertive = cut.Find("#aria-live-assertive");
        await Assert.That(assertive.GetAttribute("aria-live")).IsEqualTo("assertive");
        await Assert.That(assertive.GetAttribute("aria-atomic")).IsEqualTo("true");
    }

    [Test]
    public async Task Render_OnDefaultRoute_PreservesShellAccessibilityContract()
    {
        var cut = RenderLayout();

        cut.WaitForAssertion(() =>
        {
            _ = cut.Find("a.skip-link[href='#main-content']");
            _ = cut.Find("main#main-content[tabindex='-1']");
            _ = cut.Find("header.main-layout__header");
            _ = cut.Find("nav[aria-label='Sidebar navigation']");
            _ = cut.Find("footer.site-footer");
            _ = cut.Find("#aria-live-polite[aria-live='polite'][aria-atomic='true']");
            _ = cut.Find("#aria-live-assertive[aria-live='assertive'][aria-atomic='true']");
        });

        await Task.CompletedTask;
    }

    [Test]
    public async Task RouteChange_FocusesMainContentThroughAccessibilityService()
    {
        var cut = RenderLayout();
        var navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();
        var focusService = _ctx.Services.GetRequiredService<IAccessibilityFocusService>();

        navigationManager.NavigateTo("/events?page=2");

        cut.WaitForAssertion(() => focusService.Received(1).FocusOnNavigateAsync());

        await Task.CompletedTask;
    }

    [Test]
    public async Task WorkspaceSwitch_FocusesHeadingOrMainThroughAccessibilityService()
    {
        var cut = RenderLayout();
        var navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();
        var focusService = _ctx.Services.GetRequiredService<IAccessibilityFocusService>();
        focusService.ClearReceivedCalls();

        navigationManager.NavigateTo("/ai");

        cut.WaitForAssertion(() => focusService.Received(1).FocusOnNavigateAsync());
        await Task.CompletedTask;
    }

    [Test]
    public async Task HiddenChromeRoute_PreservesSkipMainAndLiveRegionsWhileHidingShellLandmarks()
    {
        var cut = RenderLayout();
        var navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();

        navigationManager.NavigateTo("/setup");

        cut.WaitForAssertion(() =>
        {
            _ = cut.Find("a.skip-link[href='#main-content']");
            _ = cut.Find("main#main-content[tabindex='-1']");
            _ = cut.Find("#aria-live-polite[aria-live='polite'][aria-atomic='true']");
            _ = cut.Find("#aria-live-assertive[aria-live='assertive'][aria-atomic='true']");

            if (cut.FindAll("header.main-layout__header").Count > 0)
                throw new InvalidOperationException("Expected header landmark to be hidden on setup route.");

            if (cut.FindAll("footer.site-footer").Count > 0)
                throw new InvalidOperationException("Expected footer landmark to be hidden on setup route.");

            if (cut.FindAll("nav[aria-label='Application workspaces']").Count > 0)
                throw new InvalidOperationException("Expected workspace rail to be hidden on setup route.");

        });

        await Task.CompletedTask;
    }

    #endregion

    #region Chrome Visibility

    [Test]
    public async Task Render_OnDefaultRoute_ShowsHeaderAndSidebar()
    {
        var cut = RenderLayout();

        var root = cut.Find(".main-layout-root");
        await Assert.That(root.ClassList.Contains("main-layout-root--hide-chrome")).IsFalse();
        await Assert.That(root.ClassList.Contains("main-layout-root--has-rail")).IsTrue();

        // Header present on default route
        var headers = cut.FindAll("header.main-layout__header");
        await Assert.That(headers.Count).IsGreaterThan(0);
        await Assert.That(cut.FindAll("nav[aria-label='Application workspaces']").Count).IsEqualTo(1);
    }

    [Test]
    public async Task NavigateToSetup_HidesChrome()
    {
        var cut = RenderLayout();
        var nav = _ctx.Services.GetRequiredService<NavigationManager>();

        nav.NavigateTo("/setup");

        cut.WaitForAssertion(() =>
        {
            var root = cut.Find(".main-layout-root");
            if (!root.ClassList.Contains("main-layout-root--hide-chrome"))
                throw new InvalidOperationException("Expected hide-chrome class on /setup route");
        });

        await Task.CompletedTask;
    }

    [Test]
    public async Task NavigateToOnboarding_HidesChrome()
    {
        var cut = RenderLayout();
        var nav = _ctx.Services.GetRequiredService<NavigationManager>();

        nav.NavigateTo("/onboarding/step-1");

        cut.WaitForAssertion(() =>
        {
            var root = cut.Find(".main-layout-root");
            if (!root.ClassList.Contains("main-layout-root--hide-chrome"))
                throw new InvalidOperationException("Expected hide-chrome class on /onboarding route");
        });

        await Task.CompletedTask;
    }

    [Test]
    public async Task NavigateToStartup_HidesChrome()
    {
        var cut = RenderLayout();
        var nav = _ctx.Services.GetRequiredService<NavigationManager>();

        nav.NavigateTo("/startup");

        cut.WaitForAssertion(() =>
        {
            var root = cut.Find(".main-layout-root");
            if (!root.ClassList.Contains("main-layout-root--hide-chrome"))
                throw new InvalidOperationException("Expected hide-chrome class on /startup route");
        });

        await Task.CompletedTask;
    }

    [Test]
    public async Task NavigateFromHiddenRoute_ToNormalRoute_RestoresChrome()
    {
        var cut = RenderLayout();
        var nav = _ctx.Services.GetRequiredService<NavigationManager>();

        // Navigate to setup — chrome hides
        nav.NavigateTo("/setup");
        cut.WaitForAssertion(() =>
        {
            var root = cut.Find(".main-layout-root");
            if (!root.ClassList.Contains("main-layout-root--hide-chrome"))
                throw new InvalidOperationException("Expected hide-chrome class on /setup");
        });

        // Navigate back to normal route — chrome restores
        nav.NavigateTo("/events");
        cut.WaitForAssertion(() =>
        {
            var root = cut.Find(".main-layout-root");
            if (root.ClassList.Contains("main-layout-root--hide-chrome"))
                throw new InvalidOperationException("Expected chrome restored on /events");
        });

        await Task.CompletedTask;
    }

    #endregion

    #region Shell Dock Bridge

    [Test]
    public async Task Render_RegistersShellDockPanelsAndRendersThem()
    {
        var cut = RenderLayout();
        var dockLayoutState = _ctx.Services.GetRequiredService<DockLayoutState>();

        var workspaceNav = dockLayoutState.GetPanel(ShellDockPanels.WorkspaceNavId);
        var aiAssistant = dockLayoutState.GetPanel(ShellDockPanels.AiAssistantId);

        await Assert.That(workspaceNav).IsNotNull();
        await Assert.That(workspaceNav!.Descriptor.Scope).IsEqualTo(DockScope.Shell);
        await Assert.That(workspaceNav.Descriptor.Side).IsEqualTo(DockSide.Start);
        await Assert.That(workspaceNav.Descriptor.AriaLabel).IsEqualTo("Sidebar navigation");
        await Assert.That(workspaceNav.State.IsOpen).IsTrue();

        await Assert.That(aiAssistant).IsNotNull();
        await Assert.That(aiAssistant!.Descriptor.Scope).IsEqualTo(DockScope.Shell);
        await Assert.That(aiAssistant.Descriptor.Side).IsEqualTo(DockSide.End);
        await Assert.That(aiAssistant.State.IsOpen).IsFalse();

        var shellHost = cut.Find("[data-testid='dock-layout-host'][data-dock-scope='shell']");
        await Assert.That(shellHost.ClassList.Contains("dock-layout-host--has-start")).IsTrue();
        await Assert.That(cut.FindAll("[data-testid='dock-panel-host'][data-dock-panel-id='shell.workspace-nav']").Count).IsEqualTo(1);

        var sidebarToggle = cut.Find(".navbar__sidebar-toggle");
        await Assert.That(sidebarToggle.GetAttribute("aria-controls")).IsNull();
    }

    [Test]
    public async Task FirstRender_HydratesShellDockLayoutAfterDescriptorsRegister()
    {
        _dockLayoutPersistence.LoadAsync("shell", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DockLayoutSnapshot?>(CreateShellSnapshot(workspaceNavOpen: false, aiAssistantOpen: true)));

        var cut = RenderLayout();
        var dockLayoutState = _ctx.Services.GetRequiredService<DockLayoutState>();

        cut.WaitForAssertion(() =>
        {
            if (dockLayoutState.GetPanel(ShellDockPanels.WorkspaceNavId)?.State.IsOpen == true)
                throw new InvalidOperationException("Expected shell snapshot to restore the workspace nav closed state.");

            var aiAssistant = dockLayoutState.GetPanel(ShellDockPanels.AiAssistantId);
            if (aiAssistant?.State is not { IsOpen: true, Width: 420 })
                throw new InvalidOperationException("Expected shell snapshot to restore the AI assistant state.");
        });

        _dockLayoutPersistence.Received(1).LoadAsync("shell", Arg.Any<CancellationToken>()).GetAwaiter().GetResult();
        await Task.CompletedTask;
    }

    [Test]
    public async Task NavMenuSidebarToggle_MirrorsWorkspaceNavDockPanelByShellId()
    {
        var cut = RenderLayout();
        var dockLayoutState = _ctx.Services.GetRequiredService<DockLayoutState>();

        cut.WaitForElement(".navbar__sidebar-toggle");
        cut.Find(".navbar__sidebar-toggle").Click();

        cut.WaitForAssertion(() =>
        {
            if (dockLayoutState.GetPanel(ShellDockPanels.WorkspaceNavId)?.State.IsOpen == true)
                throw new InvalidOperationException("Expected sidebar toggle to close the shell workspace-nav dock panel.");
        });

        cut.Find(".navbar__sidebar-toggle").Click();

        cut.WaitForAssertion(() =>
        {
            if (dockLayoutState.GetPanel(ShellDockPanels.WorkspaceNavId)?.State.IsOpen != true)
                throw new InvalidOperationException("Expected sidebar toggle to reopen the shell workspace-nav dock panel.");
        });

        await Task.CompletedTask;
    }

    [Test]
    public async Task AiAssistantState_WhenOpened_MirrorsAiDockPanelOpenState()
    {
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "AI Dock Bridge User");
        PublicExperienceSettingsDto settings = new PublicExperienceSettingsBuilder().WithAiAssistant();
        _publicExperienceService.GetCachedSettingsAsync().Returns(settings);
        _publicExperienceService.GetSettingsAsync().Returns(settings);
        var cut = RenderLayout();
        var aiAssistantState = _ctx.Services.GetRequiredService<AiAssistantState>();
        var dockLayoutState = _ctx.Services.GetRequiredService<DockLayoutState>();

        await cut.InvokeAsync(() =>
        {
            aiAssistantState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true);
            aiAssistantState.Open();
        });

        await Assert.That(dockLayoutState.GetPanel(ShellDockPanels.AiAssistantId)?.State.IsOpen).IsTrue();
        cut.WaitForAssertion(() =>
        {
            _ = cut.Find("[data-testid='dock-panel-host'][data-dock-panel-id='shell.ai-assistant']");
            _ = cut.Find("[data-testid='shell-ai-rail'].ai-rail--docked.ai-rail--open");

            if (cut.Markup.Contains("main-layout__main--ai-open", StringComparison.Ordinal))
                throw new InvalidOperationException("Legacy AI margin compensation class must not render after dock migration.");
        });

        await cut.InvokeAsync(aiAssistantState.Close);

        await Assert.That(dockLayoutState.GetPanel(ShellDockPanels.AiAssistantId)?.State.IsOpen).IsFalse();
    }

    [Test]
    public async Task NavMenuAiToggle_MirrorsAiDockPanelByShellId()
    {
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "AI Dock Toggle User");
        PublicExperienceSettingsDto settings = new PublicExperienceSettingsBuilder().WithAiAssistant();
        _publicExperienceService.GetCachedSettingsAsync().Returns(settings);
        _publicExperienceService.GetSettingsAsync().Returns(settings);
        var cut = RenderLayout();
        var aiAssistantState = _ctx.Services.GetRequiredService<AiAssistantState>();
        var dockLayoutState = _ctx.Services.GetRequiredService<DockLayoutState>();

        await cut.InvokeAsync(() => aiAssistantState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true));

        cut.WaitForElement("[data-testid='shell-ai-toggle']");
        cut.Find("[data-testid='shell-ai-toggle']").Click();

        cut.WaitForAssertion(() =>
        {
            if (dockLayoutState.GetPanel(ShellDockPanels.AiAssistantId)?.State.IsOpen != true)
                throw new InvalidOperationException("Expected AI toggle to open the shell AI dock panel.");
        });

        cut.Find("[data-testid='shell-ai-toggle']").Click();

        cut.WaitForAssertion(() =>
        {
            if (dockLayoutState.GetPanel(ShellDockPanels.AiAssistantId)?.State.IsOpen == true)
                throw new InvalidOperationException("Expected AI toggle to close the shell AI dock panel.");
        });
    }

    [Test]
    public async Task NavigateToHiddenChromeRoute_ClosesShellDockPanels()
    {
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Hidden Chrome Dock Bridge User");
        PublicExperienceSettingsDto settings = new PublicExperienceSettingsBuilder().WithAiAssistant();
        _publicExperienceService.GetCachedSettingsAsync().Returns(settings);
        _publicExperienceService.GetSettingsAsync().Returns(settings);
        var cut = RenderLayout();
        var aiAssistantState = _ctx.Services.GetRequiredService<AiAssistantState>();
        var dockLayoutState = _ctx.Services.GetRequiredService<DockLayoutState>();
        var navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();

        await cut.InvokeAsync(() =>
        {
            aiAssistantState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true);
            aiAssistantState.Open();
        });

        await Assert.That(dockLayoutState.GetPanel(ShellDockPanels.WorkspaceNavId)?.State.IsOpen).IsTrue();
        await Assert.That(dockLayoutState.GetPanel(ShellDockPanels.AiAssistantId)?.State.IsOpen).IsTrue();

        navigationManager.NavigateTo("/setup");

        cut.WaitForAssertion(() =>
        {
            var workspaceNav = dockLayoutState.GetPanel(ShellDockPanels.WorkspaceNavId);
            var aiAssistant = dockLayoutState.GetPanel(ShellDockPanels.AiAssistantId);

            if (workspaceNav?.State.IsOpen == true || aiAssistant?.State.IsOpen == true)
                throw new InvalidOperationException("Expected hidden chrome route to close shell dock panels.");
        });
    }

    [Test]
    public async Task NavigateThroughAiWorkspace_SuppressesThenRestoresOpenAiDock()
    {
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "AI Workspace User");
        PublicExperienceSettingsDto settings = new PublicExperienceSettingsBuilder().WithAiAssistant();
        _publicExperienceService.GetCachedSettingsAsync().Returns(settings);
        _publicExperienceService.GetSettingsAsync().Returns(settings);
        var cut = RenderLayout();
        var aiAssistantState = _ctx.Services.GetRequiredService<AiAssistantState>();
        var dockLayoutState = _ctx.Services.GetRequiredService<DockLayoutState>();
        var navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();

        await cut.InvokeAsync(() =>
        {
            aiAssistantState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true);
            aiAssistantState.Open();
        });

        navigationManager.NavigateTo("/ai");

        cut.WaitForAssertion(() =>
        {
            if (dockLayoutState.GetPanel(ShellDockPanels.AiAssistantId)?.State.IsOpen == true)
                throw new InvalidOperationException("Expected AI workspace to suppress the duplicate shell AI dock.");
            if (!aiAssistantState.IsOpen)
                throw new InvalidOperationException("Expected AI workspace handoff to retain the dock open intent.");
        });

        navigationManager.NavigateTo("/events");

        cut.WaitForAssertion(() =>
        {
            if (dockLayoutState.GetPanel(ShellDockPanels.AiAssistantId)?.State.IsOpen != true)
                throw new InvalidOperationException("Expected leaving AI workspace to restore the retained AI dock.");
        });

        await Task.CompletedTask;
    }

    [Test]
    public async Task HiddenChromeRoundTrip_PreservesUserClosedWorkspaceNavigation()
    {
        var cut = RenderLayout();
        var dockLayoutState = _ctx.Services.GetRequiredService<DockLayoutState>();
        var navigationManager = _ctx.Services.GetRequiredService<NavigationManager>();

        cut.WaitForAssertion(() =>
            _dockLayoutPersistence.Received(1).LoadAsync("shell", Arg.Any<CancellationToken>()).GetAwaiter().GetResult());

        await cut.InvokeAsync(() => dockLayoutState.Close(ShellDockPanels.WorkspaceNavId));
        await cut.InvokeAsync(() => navigationManager.NavigateTo("/setup"));
        await cut.InvokeAsync(() => navigationManager.NavigateTo("/events"));

        await Assert.That(dockLayoutState.GetPanel(ShellDockPanels.WorkspaceNavId)?.State.IsOpen).IsFalse();
    }

    [Test]
    public async Task Dispose_UnregistersShellDockPanelDescriptors()
    {
        var cut = RenderLayout();
        var dockLayoutState = _ctx.Services.GetRequiredService<DockLayoutState>();

        await Assert.That(dockLayoutState.GetPanel(ShellDockPanels.WorkspaceNavId)).IsNotNull();
        await Assert.That(dockLayoutState.GetPanel(ShellDockPanels.AiAssistantId)).IsNotNull();

        cut.Instance.Dispose();

        await Assert.That(dockLayoutState.GetPanel(ShellDockPanels.WorkspaceNavId)).IsNull();
        await Assert.That(dockLayoutState.GetPanel(ShellDockPanels.AiAssistantId)).IsNull();
    }

    [Test]
    [Arguments("events", 375)]
    [Arguments("ai", 520)]
    [Arguments("settings", 560)]
    [Arguments("studio", 720)]
    public async Task ResolveWorkspaceContentFloor_ReturnsDocumentedGenericHint(string workspace, int expected)
    {
        await Assert.That(MainLayout.ResolveWorkspaceContentFloor(new WorkspaceKey(workspace))).IsEqualTo(expected);
    }

    #endregion

    #region AI Assistant

    [Test]
    public async Task OnFirstRender_WhenAiAssistantUnavailable_HidesAiToggleAndRail()
    {
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "AI Baseline User");
        PublicExperienceSettingsDto settings = new PublicExperienceSettingsBuilder().WithAiAssistant(false);
        _publicExperienceService.GetCachedSettingsAsync().Returns(settings);
        _publicExperienceService.GetSettingsAsync().Returns(settings);

        var cut = RenderLayout();

        cut.WaitForAssertion(() =>
        {
            if (cut.FindAll("[data-testid='shell-ai-toggle']").Count > 0)
                throw new InvalidOperationException("Expected AI toggle to be hidden when unavailable.");
        });

        await Assert.That(cut.FindAll("[data-testid='shell-ai-rail']").Count).IsEqualTo(0);
    }

    [Test]
    public async Task AiToggle_WhenAvailable_OpensAndClosesAiRail()
    {
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "AI Baseline User");
        PublicExperienceSettingsDto settings = new PublicExperienceSettingsBuilder().WithAiAssistant();
        _publicExperienceService.GetCachedSettingsAsync().Returns(settings);
        _publicExperienceService.GetSettingsAsync().Returns(settings);

        var cut = RenderLayout();
        var aiAssistantState = _ctx.Services.GetRequiredService<AiAssistantState>();

        await cut.InvokeAsync(() => aiAssistantState.SetPolicy(tenantEnabled: true, tenantAvailable: true, allowAnonymousAccess: false, isAuthenticated: true));

        cut.WaitForElement("[data-testid='shell-ai-toggle']");
        var toggle = cut.Find("[data-testid='shell-ai-toggle']");

        await Assert.That(toggle.GetAttribute("aria-controls")).IsEqualTo("ai-assistant-rail");
        await Assert.That(toggle.GetAttribute("aria-expanded")).IsEqualTo("false");

        toggle.Click();

        cut.WaitForAssertion(() =>
        {
            var rail = cut.Find("[data-testid='shell-ai-rail']");
            if (!rail.ClassList.Contains("ai-rail--open"))
                throw new InvalidOperationException("Expected AI rail to open after toggle click.");

            var updatedToggle = cut.Find("[data-testid='shell-ai-toggle']");
            if (updatedToggle.GetAttribute("aria-expanded") != "true")
                throw new InvalidOperationException("Expected AI toggle aria-expanded to be true.");
        });

        cut.Find("[data-testid='shell-ai-toggle']").Click();

        cut.WaitForAssertion(() =>
        {
            var rails = cut.FindAll("[data-testid='shell-ai-rail']");
            if (rails.Count > 0 && rails[0].ClassList.Contains("ai-rail--open"))
                throw new InvalidOperationException("Expected AI rail to close after second toggle click.");
        });
    }

    #endregion

    #region User Sync

    [Test]
    public async Task OnFirstRender_AuthenticatedUser_CallsSyncUser()
    {
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Test User");

        var cut = RenderLayout();

        cut.WaitForAssertion(() =>
            _userService.Received(1).SyncUserAsync());

        await Task.CompletedTask;
    }

    [Test]
    public async Task OnFirstRender_AnonymousUser_DoesNotCallSyncUser()
    {
        _ctx.SetAnonymousUser();

        var cut = RenderLayout();

        // Wait for async lifecycle to complete (settings load proves OnAfterRenderAsync ran)
        cut.WaitForAssertion(() =>
            _publicExperienceService.Received().GetCachedSettingsAsync());

        // Anonymous user should not trigger user sync
        await _userService.DidNotReceive().SyncUserAsync();
    }

    [Test]
    public async Task OnFirstRender_WhenCachedSettingsMissing_ContinuesTenantAndThemeInitialization()
    {
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Null Settings Lifecycle User");
        _publicExperienceService.GetCachedSettingsAsync().Returns((PublicExperienceSettingsDto?)null);
        var tenantNavigationService = _ctx.Services.GetRequiredService<ITenantNavigationService>();

        var cut = RenderLayout();

        cut.WaitForAssertion(() =>
            tenantNavigationService.Received().GetNavigationLinksAsync());
        cut.WaitForAssertion(() =>
            _appearanceThemeService.Received().InitializeAsync(Arg.Any<MudThemeProvider>(), Arg.Any<CancellationToken>()));
        cut.WaitForAssertion(() =>
            _appearanceThemeService.Received().ResolveEffectiveDarkModeAsync(Arg.Any<MudThemeProvider>()));

        await Task.CompletedTask;
    }

    #endregion

    #region Settings-Driven UI

    [Test]
    public async Task OnFirstRender_WithBrandName_DisplaysBrandInSidebar()
    {
        PublicExperienceSettingsDto settings = new PublicExperienceSettingsBuilder()
            .WithBranding("My Test Brand");
        _publicExperienceService.GetCachedSettingsAsync().Returns(settings);
        _publicExperienceService.GetSettingsAsync().Returns(settings);

        var cut = RenderLayout();

        cut.WaitForAssertion(() =>
        {
            var sideNav = cut.Find("[data-testid='events-workspace-navigation']");
            if (!sideNav.TextContent.Contains("My Test Brand", StringComparison.Ordinal))
                throw new InvalidOperationException("Expected brand name 'My Test Brand' in sidebar");
        });

        await Task.CompletedTask;
    }

    [Test]
    public async Task OnFirstRender_NoSubmissionPolicies_HidesCommunityGuidelinesLink()
    {
        // Model defaults AllowUser/Org/GroupSubmittedEvents to true — must explicitly disable
        PublicExperienceSettingsDto settings = new PublicExperienceSettingsBuilder()
            .WithUserSubmittedEvents(false)
            .WithOrganizationSubmittedEvents(false)
            .WithGroupSubmittedEvents(false);
        _publicExperienceService.GetCachedSettingsAsync().Returns(settings);

        var cut = RenderLayout();

        // Wait for settings to load, re-render, and community guidelines link to disappear
        cut.WaitForAssertion(() =>
        {
            var sideNav = cut.Find("[data-testid='events-workspace-navigation']");
            var links = sideNav.QuerySelectorAll("a[href='/community-guidelines']");
            if (links.Count > 0)
                throw new InvalidOperationException("Expected community guidelines link to be hidden");
        });

        await Task.CompletedTask;
    }

    #endregion

    #region Theme

    [Test]
    public async Task Render_CallsCreateTheme_DuringInitialization()
    {
        var cut = RenderLayout();

        _appearanceThemeService.Received(1).CreateTheme(Arg.Any<string>());

        await Task.CompletedTask;
    }

    [Test]
    [Arguments(false, "light")]
    [Arguments(true, "dark")]
    public async Task Render_ExposesActiveColorSchemeForThemeAwareMedia(bool isDarkMode, string expectedColorScheme)
    {
        var cut = _ctx.Render<MainLayout>(parameters => parameters
            .Add(layout => layout.Body, (RenderFragment)(builder => builder.AddContent(0, "Test body content")))
            .Add(layout => layout.InitialTheme, isDarkMode));

        var root = cut.Find(".main-layout-root");

        await Assert.That(root.GetAttribute("style")).Contains($"color-scheme: {expectedColorScheme}");
    }

    #endregion

    #region Content

    [Test]
    public async Task Render_DisplaysProvidedBodyContent()
    {
        var cut = RenderLayout();

        await Assert.That(cut.Markup).Contains("Test body content");
    }

    #endregion
}
