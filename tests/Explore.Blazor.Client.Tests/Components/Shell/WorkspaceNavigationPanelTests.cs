using Explore.Blazor.Client.Components.Shell;
using Explore.Blazor.Client.Components.Shell.Workspaces;
using Explore.Blazor.Client.Contracts.Services.Ai;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Contracts.Services.SupportAccess;
using Explore.Blazor.Client.Layout;
using Explore.Blazor.Client.Services.Ai;
using Explore.Blazor.Client.Services.Docking;
using Explore.Blazor.Client.Services.Shell;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Components.Shell;

public sealed class WorkspaceNavigationPanelTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private readonly IDockLayoutPersistence _dockLayoutPersistence;

    public WorkspaceNavigationPanelTests()
    {
        _ctx = new BlazorTestContext();

        _ctx.Services.AddScoped<AiAssistantState>();
        _ctx.Services.AddScoped<AiAssistantConversationState>();
        _ctx.Services.AddScoped<MainContentAppearanceState>();
        _ctx.Services.AddScoped<TenantNavLinksState>();
        _ctx.Services.AddScoped<DockLayoutState>();
        _ctx.Services.AddScoped<IDockPanelRegistry>(p => p.GetRequiredService<DockLayoutState>());
        _ctx.Services.AddScoped<IWorkspaceRegistry>(_ => new TestNoProviderRegistry());
        _ctx.Services.AddScoped<WorkspaceRouteClassifier>();
        _ctx.Services.AddScoped<UiShellState>();
        _ctx.Services.AddSingleton(Substitute.For<IShellPreferencesService>());

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

        var publicExperience = Substitute.For<IPublicExperienceService>();
        publicExperience.ResolveHomeRoute(Arg.Any<PublicExperienceSettingsDto?>()).Returns("/events");
        _ctx.Services.AddSingleton(publicExperience);

        var appearanceTheme = Substitute.For<IAppearanceThemeService>();
        appearanceTheme.Current.Returns(new AppearanceState());
        appearanceTheme.CreateTheme(Arg.Any<string>()).Returns(new MudTheme());
        appearanceTheme.InitializeAsync(Arg.Any<MudThemeProvider>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        appearanceTheme.ResolveEffectiveDarkModeAsync(Arg.Any<MudThemeProvider>())
            .Returns(Task.FromResult(false));
        appearanceTheme.SetThemeModeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _ctx.Services.AddSingleton(appearanceTheme);

        var supportAccess = Substitute.For<ISupportAccessClientService>();
        supportAccess.RefreshAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        supportAccess.StopCurrentAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SupportAccessCommandResult.Succeeded()));
        _ctx.Services.AddSingleton(supportAccess);

        _ctx.Services.AddSingleton(Substitute.For<IAnalyticsInterop>());
        _ctx.Services.AddSingleton(Substitute.For<ICookieConsentInterop>());
        _ctx.Services.AddSingleton(new CookieConsentStateService());
        _ctx.Services.GetRequiredService<IUiShellContextService>()
            .GetCachedContextAsync(Arg.Any<CancellationToken>())
            .Returns((UiShellContextDto?)null);
    }

    public void Dispose() => _ctx.Dispose();

    private IRenderedComponent<MainLayout> RenderLayout()
    {
        return _ctx.Render<MainLayout>(p =>
            p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "Test body content"))));
    }

    [Test]
    public async Task NoProviderWorkspace_ClosesStartPanel()
    {
        _ctx.SetAnonymousUser();
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/events");
        var cut = RenderLayout();
        var dockLayoutState = _ctx.Services.GetRequiredService<DockLayoutState>();

        cut.WaitForAssertion(() =>
        {
            if (dockLayoutState.GetPanel(ShellDockPanels.WorkspaceNavId)?.State.IsOpen != true)
                throw new InvalidOperationException("Expected panel open on Events route");
        });

        await cut.InvokeAsync(() => navigation.NavigateTo("/no-nav"));

        cut.WaitForAssertion(() =>
        {
            if (dockLayoutState.GetPanel(ShellDockPanels.WorkspaceNavId)?.State.IsOpen == true)
                throw new InvalidOperationException("Expected panel closed on no-provider workspace");

            if (cut.FindAll(".navbar__sidebar-toggle").Count != 0)
                throw new InvalidOperationException("Expected workspace navigation toggle hidden on no-provider workspace");
        });
    }

    [Test]
    public async Task ReturnFromNoProvider_ReopensStartPanel()
    {
        _ctx.SetAnonymousUser();
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/events");
        var cut = RenderLayout();
        var dockLayoutState = _ctx.Services.GetRequiredService<DockLayoutState>();

        cut.WaitForAssertion(() =>
        {
            if (dockLayoutState.GetPanel(ShellDockPanels.WorkspaceNavId)?.State.IsOpen != true)
                throw new InvalidOperationException("Expected panel open on Events route");
        });

        await cut.InvokeAsync(() => navigation.NavigateTo("/no-nav"));

        cut.WaitForAssertion(() =>
        {
            if (dockLayoutState.GetPanel(ShellDockPanels.WorkspaceNavId)?.State.IsOpen == true)
                throw new InvalidOperationException("Expected panel closed on no-provider workspace");
        });

        await cut.InvokeAsync(() => navigation.NavigateTo("/events"));

        cut.WaitForAssertion(() =>
        {
            if (dockLayoutState.GetPanel(ShellDockPanels.WorkspaceNavId)?.State.IsOpen != true)
                throw new InvalidOperationException("Expected panel reopened after returning to Events");
        });
    }

    [Test]
    public async Task UserClosedPanel_DoesNotReopenOnNoProviderReturn()
    {
        _ctx.SetAnonymousUser();
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/events");
        var cut = RenderLayout();
        var dockLayoutState = _ctx.Services.GetRequiredService<DockLayoutState>();

        cut.WaitForAssertion(() =>
        {
            if (dockLayoutState.GetPanel(ShellDockPanels.WorkspaceNavId)?.State.IsOpen != true)
                throw new InvalidOperationException("Expected panel open on Events route");
        });

        await cut.InvokeAsync(() => dockLayoutState.Close(ShellDockPanels.WorkspaceNavId));

        await Assert.That(dockLayoutState.GetPanel(ShellDockPanels.WorkspaceNavId)?.State.IsOpen).IsFalse();

        await cut.InvokeAsync(() => navigation.NavigateTo("/no-nav"));
        await cut.InvokeAsync(() => navigation.NavigateTo("/events"));

        await Assert.That(dockLayoutState.GetPanel(ShellDockPanels.WorkspaceNavId)?.State.IsOpen).IsFalse();
    }

    private static DockLayoutSnapshot CreateShellSnapshot(bool workspaceNavOpen)
    {
        return new DockLayoutSnapshot(
            "shell",
            [
                new DockPanelState(ShellDockPanels.WorkspaceNavId, workspaceNavOpen, DockMode.Docked, Width: 280, Order: 10, IsActive: workspaceNavOpen),
                new DockPanelState(ShellDockPanels.AiAssistantId, false, DockMode.Docked, Width: 360, Order: 20, IsActive: false)
            ],
            TestTime.UtcNow);
    }

    [Test]
    public async Task RestoredOpenPanel_OnNoProviderWorkspace_ClosesAfterHydration()
    {
        _ctx.SetAnonymousUser();
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/no-nav");

        _dockLayoutPersistence.LoadAsync("shell", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DockLayoutSnapshot?>(CreateShellSnapshot(workspaceNavOpen: true)));

        var cut = RenderLayout();
        var dockLayoutState = _ctx.Services.GetRequiredService<DockLayoutState>();

        cut.WaitForAssertion(() =>
        {
            if (dockLayoutState.GetPanel(ShellDockPanels.WorkspaceNavId)?.State.IsOpen == true)
                throw new InvalidOperationException("Expected restored-open panel to be closed on no-provider workspace after hydration");
        });
    }

    [Test]
    public async Task RestoredClosedPanel_DoesNotReopenOnProviderWorkspaceAfterHydration()
    {
        _ctx.SetAnonymousUser();
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/no-nav");

        _dockLayoutPersistence.LoadAsync("shell", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DockLayoutSnapshot?>(CreateShellSnapshot(workspaceNavOpen: false)));

        var cut = RenderLayout();
        var dockLayoutState = _ctx.Services.GetRequiredService<DockLayoutState>();

        cut.WaitForAssertion(() =>
        {
            if (dockLayoutState.GetPanel(ShellDockPanels.WorkspaceNavId)?.State.IsOpen == true)
                throw new InvalidOperationException("Expected restored-closed panel to stay closed on no-provider workspace after hydration");
        });

        await cut.InvokeAsync(() => navigation.NavigateTo("/events"));

        await Assert.That(dockLayoutState.GetPanel(ShellDockPanels.WorkspaceNavId)?.State.IsOpen).IsFalse();
    }

    private sealed class TestNoProviderRegistry : IWorkspaceRegistry
    {
        public IReadOnlyList<WorkspaceDescriptor> Workspaces { get; } =
        [
            new(WorkspaceKey.Events, "workspace.events", Icons.Material.Filled.Explore, "/", false, null, typeof(EventsWorkspaceNavigation)),
            new(new WorkspaceKey("no-nav"), "workspace.no-nav", Icons.Material.Filled.BugReport, "/no-nav", false, null, null)
        ];
    }
}
