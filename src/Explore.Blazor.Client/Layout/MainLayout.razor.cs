using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Shell;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Services.Docking;
using Explore.Blazor.Client.Services.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;

namespace Explore.Blazor.Client.Layout;

public partial class MainLayout : LayoutComponentBase, IDisposable
{
    private const string ShellDockLayoutKey = "shell";
    private static readonly TimeSpan DockLayoutAutosaveDelay = TimeSpan.FromMilliseconds(500);
    private const int NavbarHeightPx = 64;
    private const int AnnouncementBarHeightPx = 48;
    private const int SupportAccessBannerHeightPx = 56;

    private bool _isDarkMode = false;
    private bool _isInitialized = false;
    private bool _announcementVisible;
    private bool _supportAccessBannerVisible;
    private MudTheme? _theme;
    private MudThemeProvider _mudThemeProvider = null!;
    private bool _hideChrome;
    private bool _languagePickerEnabled = true;
    private bool _showCommunityGuidelinesLink = true;
    private string _brandDisplayName = string.Empty;
    private string _brandLogoUrl = string.Empty;
    private bool _syncingAiAssistantState;
    private bool _shellDockLayoutHydrated;
    private bool _suppressShellDockLayoutAutosave;
    private bool _closedByNoNavigation;
    private bool _workspaceNavClosedByHiddenChrome;
    private DockLayoutSnapshot? _lastPersistedShellDockLayoutSnapshot;
    private CancellationTokenSource? _shellDockLayoutAutosaveCts;
    private CancellationTokenSource? _shellSelectionAutosaveCts;
    private UiShellContextDto? _shellContext;
    private bool _shellPreferencesHydrated;

    [Inject]
    protected IUserService UserService { get; set; } = null!;

    [Inject]
    protected AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;

    [Inject]
    protected ILogger<MainLayout> Logger { get; set; } = null!;

    [Inject]
    protected NavigationManager NavigationManager { get; set; } = null!;

    [Inject]
    protected MainContentAppearanceState MainContentAppearanceState { get; set; } = null!;

    [Inject]
    protected AiAssistantState AiAssistantState { get; set; } = null!;

    [Inject]
    protected DockLayoutState DockLayoutState { get; set; } = null!;

    [Inject]
    protected IDockLayoutPersistence DockLayoutPersistence { get; set; } = null!;

    [Inject]
    protected TenantNavLinksState TenantNavLinksState { get; set; } = null!;

    [Inject]
    protected ITenantNavigationService TenantNavigationService { get; set; } = null!;

    [Inject]
    protected IPublicExperienceService PublicExperienceService { get; set; } = null!;

    [Inject]
    protected IAppearanceThemeService AppearanceThemeService { get; set; } = null!;

    [Inject]
    protected IAccessibilityFocusService AccessibilityFocusService { get; set; } = null!;

    [Inject]
    protected IWorkspaceRegistry WorkspaceRegistry { get; set; } = null!;

    [Inject]
    protected UiShellState UiShellState { get; set; } = null!;

    [Inject]
    protected IUiShellContextService ShellContextService { get; set; } = null!;

    [Inject]
    protected IShellPreferencesService ShellPreferencesService { get; set; } = null!;

    [CascadingParameter(Name = "InitialTheme")]
    public bool? InitialTheme { get; set; }

    [CascadingParameter(Name = "Language")]
    public global::Explore.Blazor.Client.Models.LanguageContext? LanguageContext { get; set; }

    private bool _isRtl => LanguageContext?.EffectiveIsRtl ?? false;

    private string MainContentClass => MainContentAppearanceState.HasAppearance
        ? "main-layout__content-wrapper main-layout__content-wrapper--themed"
        : "main-layout__content-wrapper";

    private string MainContentStyle => MainContentAppearanceState.Style;

    private string ActiveColorScheme => _isDarkMode ? "dark" : "light";

    private bool ActiveWorkspaceHasNavigation => WorkspaceRegistry.Workspaces.Any(workspace =>
        workspace.Key == UiShellState.ActiveWorkspace && workspace.NavigationProviderType is not null);

    private bool IsAiWorkspace => UiShellState.ActiveWorkspace == WorkspaceKey.Ai;

    private int ActiveWorkspaceContentFloor => ResolveWorkspaceContentFloor(UiShellState.ActiveWorkspace);

    public string DarkLightModeButtonIcon => _isDarkMode switch
    {
        true => Icons.Material.Rounded.AutoMode,
        false => Icons.Material.Outlined.DarkMode,
    };

    protected override void OnInitialized()
    {
        base.OnInitialized();

        RegisterShellDockPanels();

        NavigationManager.LocationChanged += OnLocationChanged;
        MainContentAppearanceState.Changed += OnMainContentAppearanceChanged;
        AiAssistantState.OnChange += OnAiAssistantStateChanged;
        TenantNavLinksState.OnChange += OnTenantNavLinksChanged;
        DockLayoutState.Changed += OnDockLayoutChanged;
        UiShellState.Changed += OnShellStateChanged;
        UpdateChromeVisibility();
        if (!_hideChrome
            && !IsAuthenticationEntryRoute
            && ActiveWorkspaceHasNavigation)
        {
            SyncWorkspaceNavigationPolicyState(shouldBeOpen: true);
        }
        SyncWorkspaceNavigationPanel();

        if (InitialTheme.HasValue)
        {
            _isDarkMode = InitialTheme.Value;
        }

        _theme = AppearanceThemeService.CreateTheme(GetAppbarHeight());
        AppearanceThemeService.Changed += OnAppearanceChanged;
        StateHasChanged();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            var isAuthenticated = false;

            try
            {
                var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
                isAuthenticated = authState.User.Identity?.IsAuthenticated == true;
                if (isAuthenticated)
                {
                    await UserService.SyncUserAsync();
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Error syncing user");
            }

            await HydrateShellPreferencesAsync();
            await HydrateShellDockLayoutAsync();

            try
            {
                var settings = await PublicExperienceService.GetCachedSettingsAsync();
                if (settings is not null)
                {
                    _showCommunityGuidelinesLink = settings.AllowUserSubmittedEvents == true
                        || settings.AllowOrganizationSubmittedEvents == true
                        || settings.AllowGroupSubmittedEvents == true;
                    _brandDisplayName = settings.BrandDisplayName ?? string.Empty;
                    _brandLogoUrl = settings.BrandLogoUrl ?? string.Empty;
                    _languagePickerEnabled = settings.ClientPickerEnabled ?? true;
                    AiAssistantState.SetPolicy(
                        settings.IsAiAssistantEnabled == true,
                        settings.IsAiAssistantAvailable == true,
                        settings.AiAssistantAllowAnonymousAccess == true,
                        isAuthenticated);
                    DockLayoutState.Refresh();
                    await InvokeAsync(StateHasChanged);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Error loading public experience settings for sidebar");
            }

            try
            {
                await TenantNavLinksState.EnsureLoadedAsync(TenantNavigationService);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Error loading tenant navigation links for sidebar");
            }

            try
            {
                var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
                if (authState.User.Identity?.IsAuthenticated == true)
                {
                    await AppearanceThemeService.InitializeAsync(_mudThemeProvider);
                    _theme = AppearanceThemeService.CreateTheme(GetAppbarHeight());
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Could not initialize appearance theme service.");
            }

            if (!InitialTheme.HasValue)
            {
                try
                {
                    _isDarkMode = await AppearanceThemeService.ResolveEffectiveDarkModeAsync(_mudThemeProvider);
                    await InvokeAsync(StateHasChanged);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Error initializing theme");
                }
            }

            _isInitialized = true;
        }
    }

    private async Task DarkModeToggle()
    {
        _isDarkMode = !_isDarkMode;
        var mode = _isDarkMode ? "dark" : "light";
        await AppearanceThemeService.SetThemeModeAsync(mode);
    }

    private void OnAppearanceChanged(object? sender, AppearanceStateChangedEventArgs e)
    {
        _theme = AppearanceThemeService.CreateTheme(GetAppbarHeight());
        var mode = e.State.ThemeMode.ToLowerInvariant();
        if (mode is "dark" or "darkhighcontrast") _isDarkMode = true;
        else if (mode is "light" or "lighthighcontrast") _isDarkMode = false;
        InvokeAsync(StateHasChanged);
    }

    private void OnMainContentAppearanceChanged()
    {
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        UpdateChromeVisibility();
        SyncWorkspaceNavigationPanel();
        _ = InvokeAsync(async () =>
        {
            StateHasChanged();
            await AccessibilityFocusService.FocusOnNavigateAsync();
        });
    }

    private void OnShellStateChanged()
    {
        ApplyGovernedWorkspaceNavigationMode(forceDefault: false);
        SyncWorkspaceNavigationPanel();
        ScheduleShellSelectionAutosave();
        _ = InvokeAsync(StateHasChanged);
    }

    private void SyncWorkspaceNavigationPanel()
    {
        if (_hideChrome)
        {
            return;
        }

        var hasNavigation = ActiveWorkspaceHasNavigation;
        var panel = DockLayoutState.GetPanel(ShellDockPanels.WorkspaceNavId);
        var isPanelOpen = panel?.State.IsOpen == true;

        if (!hasNavigation && isPanelOpen)
        {
            _closedByNoNavigation = true;
            SyncWorkspaceNavigationPolicyState(shouldBeOpen: false);
        }
        else if (hasNavigation)
        {
            if (_closedByNoNavigation && !isPanelOpen)
            {
                _closedByNoNavigation = false;
                SyncWorkspaceNavigationPolicyState(shouldBeOpen: true);
            }
            else if (isPanelOpen)
            {
                _closedByNoNavigation = false;
            }
        }
    }

    private void UpdateChromeVisibility()
    {
        var relative = NavigationManager.ToBaseRelativePath(NavigationManager.Uri);
        var path = relative.Split('?', '#')[0];

        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        if (path.Length > 1)
        {
            path = path.TrimEnd('/');
        }

        var wasHidden = _hideChrome;

        _hideChrome = path.Equals("/setup", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/onboarding/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/startup", StringComparison.OrdinalIgnoreCase);

        if (_hideChrome)
        {
            if (!wasHidden)
            {
                _workspaceNavClosedByHiddenChrome = DockLayoutState
                    .GetPanel(ShellDockPanels.WorkspaceNavId)?.State.IsOpen == true;
            }

            SyncWorkspaceNavigationPolicyState(shouldBeOpen: false);
        }
        else if (wasHidden)
        {
            var shouldRestoreWorkspaceNav = _workspaceNavClosedByHiddenChrome;
            _workspaceNavClosedByHiddenChrome = false;

            if (shouldRestoreWorkspaceNav && !DockLayoutState.IsMobileViewport && ActiveWorkspaceHasNavigation)
            {
                SyncWorkspaceNavigationPolicyState(shouldBeOpen: true);
            }
        }

        SyncAiAssistantDockState();
    }


    private bool IsAuthenticationEntryRoute
    {
        get
        {
            string path = NavigationManager.ToBaseRelativePath(
                    NavigationManager.Uri)
                .Split('?', '#')[0]
                .TrimEnd('/');
            return path.Equals("login", StringComparison.OrdinalIgnoreCase);
        }
    }

    private void RegisterShellDockPanels()
    {
        DockLayoutState.Register(ShellDockPanels.WorkspaceNav, RenderShellWorkspaceNav);
        DockLayoutState.Register(ShellDockPanels.AiAssistant, RenderShellAiAssistant);
    }

    private void RenderShellWorkspaceNav(RenderTreeBuilder builder)
    {
        builder.OpenComponent<WorkspaceNavigationHost>(0);
        builder.AddAttribute(1, "BrandDisplayName", _brandDisplayName);
        builder.AddAttribute(2, "BrandLogoUrl", _brandLogoUrl);
        builder.AddAttribute(3, "OnCloseRequested", EventCallback.Factory.Create(this, CloseShellWorkspaceNav));
        builder.CloseComponent();
    }

    private static void RenderShellAiAssistant(RenderTreeBuilder builder)
    {
        builder.OpenComponent<AiAssistantRail>(0);
        builder.AddAttribute(1, "HostedInDock", true);
        builder.CloseComponent();
    }

    private void OnAiAssistantStateChanged()
    {
        if (_syncingAiAssistantState)
        {
            return;
        }

        SyncAiAssistantDockState();
        StateHasChanged();
    }

    private void OnDockLayoutChanged()
    {
        var workspaceNavigation = DockLayoutState.GetPanel(
            ShellDockPanels.WorkspaceNavId);
        if (workspaceNavigation?.State.IsOpen == true
            && (DockLayoutState.IsMobileViewport
                || DockLayoutState.ShouldRenderDockedPanelAsOverlay(
                    workspaceNavigation)))
        {
            SyncWorkspaceNavigationPolicyState(shouldBeOpen: false);
            return;
        }

        var aiOpen = DockLayoutState.GetPanel(ShellDockPanels.AiAssistantId)?.State.IsOpen == true;

        if (!_syncingAiAssistantState)
        {
            _syncingAiAssistantState = true;
            try
            {
                if (aiOpen && !AiAssistantState.IsOpen)
                {
                    AiAssistantState.Open();
                }
                else if (!aiOpen && AiAssistantState.IsOpen)
                {
                    AiAssistantState.Close();
                }
            }
            finally
            {
                _syncingAiAssistantState = false;
            }
        }

        if (ShouldAutosaveShellDockLayout())
        {
            ScheduleShellDockLayoutAutosave();
        }
        _ = InvokeAsync(StateHasChanged);
    }

    private bool ShouldAutosaveShellDockLayout()
    {
        return DockLayoutState.LastChangeReason is DockLayoutChangeReason.UserAction or DockLayoutChangeReason.Reset;
    }

    private async Task HydrateShellDockLayoutAsync()
    {
        _suppressShellDockLayoutAutosave = true;

        try
        {
            var snapshot = await DockLayoutPersistence.LoadAsync(ShellDockLayoutKey);
            if (snapshot is not null)
            {
                DockLayoutState.RestoreSnapshot(snapshot, ShellDockLayoutKey, DockScope.Shell);
                _closedByNoNavigation = false;
                SyncWorkspaceNavigationPanel();
            }

            ApplyGovernedWorkspaceNavigationMode(forceDefault: snapshot is null);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to hydrate shell dock layout.");
        }
        finally
        {
            _lastPersistedShellDockLayoutSnapshot = CreateShellDockLayoutSnapshot();
            _shellDockLayoutHydrated = true;
            _suppressShellDockLayoutAutosave = false;
        }
    }

    private void ScheduleShellDockLayoutAutosave()
    {
        if (!_shellDockLayoutHydrated || _suppressShellDockLayoutAutosave || !HasShellDockLayoutChanged())
        {
            return;
        }

        _shellDockLayoutAutosaveCts?.Cancel();
        _shellDockLayoutAutosaveCts?.Dispose();

        var autosaveCts = new CancellationTokenSource();
        _shellDockLayoutAutosaveCts = autosaveCts;
        var snapshot = CreateShellDockLayoutSnapshot();
        _ = PersistShellDockLayoutAfterDelayAsync(autosaveCts, snapshot);
    }

    private async Task PersistShellDockLayoutAfterDelayAsync(
        CancellationTokenSource autosaveCts,
        DockLayoutSnapshot snapshot)
    {
        try
        {
            await Task.Delay(DockLayoutAutosaveDelay, autosaveCts.Token);
            if (SnapshotPanelsEqual(_lastPersistedShellDockLayoutSnapshot, snapshot))
            {
                return;
            }

            await DockLayoutPersistence.SaveAsync(snapshot, autosaveCts.Token);
            _lastPersistedShellDockLayoutSnapshot = snapshot;
        }
        catch (OperationCanceledException) when (autosaveCts.IsCancellationRequested)
        {
            // A newer dock layout change superseded this pending autosave.
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to save shell dock layout.");
        }
    }

    private async Task ResetShellDockLayoutAsync()
    {
        _suppressShellDockLayoutAutosave = true;

        try
        {
            ResetShellPanelToDefaults(ShellDockPanels.WorkspaceNav);
            ResetShellPanelToDefaults(ShellDockPanels.AiAssistant);
            await DockLayoutPersistence.DeleteAsync(ShellDockLayoutKey);
            _lastPersistedShellDockLayoutSnapshot = CreateShellDockLayoutSnapshot();
            SyncAiAssistantDockState();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to reset shell dock layout.");
        }
        finally
        {
            _suppressShellDockLayoutAutosave = false;
            _shellDockLayoutHydrated = true;
        }
    }

    private void ResetShellPanelToDefaults(DockPanelDescriptor descriptor)
    {
        var entry = DockLayoutState.GetPanel(descriptor.Id);
        if (entry is null)
        {
            return;
        }

        if (entry.State.IsOpen && descriptor.CanClose)
        {
            DockLayoutState.Close(descriptor.Id);
        }

        DockLayoutState.SetMode(descriptor.Id, descriptor.DefaultMode);

        if (descriptor.IsResizable)
        {
            DockLayoutState.Resize(descriptor.Id, descriptor.DefaultWidth);
        }
    }

    private DockLayoutSnapshot CreateShellDockLayoutSnapshot()
    {
        return DockLayoutState.CreateSnapshot(ShellDockLayoutKey, DockScope.Shell);
    }

    private bool HasShellDockLayoutChanged()
    {
        return !SnapshotPanelsEqual(_lastPersistedShellDockLayoutSnapshot, CreateShellDockLayoutSnapshot());
    }

    private async Task HydrateShellPreferencesAsync()
    {
        try
        {
            _shellContext = await ShellContextService.GetCachedContextAsync();
            if (_shellContext is null)
            {
                return;
            }

            var preferences = await ShellPreferencesService.LoadAsync(_shellContext);
            UiShellState.ReconcileAvailability(workspace => IsWorkspaceAvailable(workspace, _shellContext));
            UiShellState.ReconcileActiveActors(_shellContext.ManagedActors, _shellContext.PinnedActorId);
            if (!_shellContext.PinnedActorId.HasValue && preferences.LastActorId.HasValue)
            {
                UiShellState.TrySetActiveActor(preferences.LastActorId.Value, _shellContext.ManagedActors);
            }

            var workspace = WorkspaceRegistry.Workspaces.FirstOrDefault(candidate =>
                candidate.Key.Value.Equals(preferences.LastWorkspace, StringComparison.OrdinalIgnoreCase));
            if (workspace is not null && IsWorkspaceAvailable(workspace.Key, _shellContext))
            {
                UiShellState.RestoreLastRoute(workspace.Key, workspace.BaseRoute);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to hydrate shell selection preferences.");
        }
        finally
        {
            _shellPreferencesHydrated = true;
        }
    }

    private void ApplyGovernedWorkspaceNavigationMode(bool forceDefault)
    {
        var defaults = _shellContext?.NavigationDefaults;
        if (defaults is null || (!forceDefault && defaults.AllowUserOverride != false))
        {
            return;
        }

        var configuredMode = UiShellState.ActiveWorkspace.Value switch
        {
            "events" => defaults.Events,
            "studio" => defaults.Studio,
            "ai" => defaults.Ai,
            _ => null
        };
        if (configuredMode is null || DockLayoutState.GetPanel(ShellDockPanels.WorkspaceNavId) is null)
        {
            return;
        }

        var mode = configuredMode.Equals("Collapsed", StringComparison.OrdinalIgnoreCase)
            ? DockMode.Collapsed
            : DockMode.Docked;
        DockLayoutState.SetMode(
            ShellDockPanels.WorkspaceNavId,
            mode,
            DockLayoutChangeReason.Refresh);
    }

    private void ScheduleShellSelectionAutosave()
    {
        if (!_shellPreferencesHydrated)
        {
            return;
        }

        _shellSelectionAutosaveCts?.Cancel();
        _shellSelectionAutosaveCts?.Dispose();
        var autosaveCts = new CancellationTokenSource();
        _shellSelectionAutosaveCts = autosaveCts;
        var workspace = UiShellState.ActiveWorkspace.Value;
        var actorId = UiShellState.ActiveActorId;
        var currentRoute = GetCurrentRoute();
        _ = PersistShellSelectionAfterDelayAsync(
            autosaveCts,
            workspace,
            actorId,
            currentRoute);
    }

    private async Task PersistShellSelectionAfterDelayAsync(
        CancellationTokenSource autosaveCts,
        string workspace,
        Guid? actorId,
        string currentRoute)
    {
        try
        {
            await Task.Delay(DockLayoutAutosaveDelay, autosaveCts.Token);
            await ShellPreferencesService.SaveSelectionAsync(
                workspace,
                actorId,
                currentRoute,
                autosaveCts.Token);
        }
        catch (OperationCanceledException) when (autosaveCts.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to save shell selection preferences.");
        }
    }

    private string GetCurrentRoute()
    {
        var relative = NavigationManager.ToBaseRelativePath(NavigationManager.Uri);
        return string.IsNullOrWhiteSpace(relative) ? "/" : $"/{relative.TrimStart('/')}";
    }

    private static bool IsWorkspaceAvailable(WorkspaceKey workspace, UiShellContextDto context) =>
        workspace.Value switch
        {
            "events" => true,
            "studio" => context.Workspaces?.Studio == true,
            "ai" => context.Workspaces?.Ai == true,
            "settings" => true,
            _ => false
        };

    internal static int ResolveWorkspaceContentFloor(WorkspaceKey workspace) => workspace.Value switch
    {
        "ai" => 520,
        "settings" => 560,
        "studio" => 720,
        _ => 375
    };

    private static bool SnapshotPanelsEqual(DockLayoutSnapshot? previous, DockLayoutSnapshot current)
    {
        return previous is not null && previous.Panels.SequenceEqual(current.Panels);
    }

    private void CloseShellWorkspaceNav()
    {
        SyncPanelState(ShellDockPanels.WorkspaceNavId, shouldBeOpen: false, reason: DockLayoutChangeReason.UserAction);
    }

    private void SyncWorkspaceNavigationPolicyState(bool shouldBeOpen)
    {
        var wasAutosaveSuppressed = _suppressShellDockLayoutAutosave;
        _suppressShellDockLayoutAutosave = true;

        try
        {
            var panel = DockLayoutState.GetPanel(ShellDockPanels.WorkspaceNavId);
            if (shouldBeOpen && panel is not null && (DockLayoutState.IsMobileViewport || DockLayoutState.ShouldRenderDockedPanelAsOverlay(panel)))
            {
                shouldBeOpen = false;
            }

            SyncPanelState(ShellDockPanels.WorkspaceNavId, shouldBeOpen, reason: DockLayoutChangeReason.ViewportPolicy);
        }
        finally
        {
            _suppressShellDockLayoutAutosave = wasAutosaveSuppressed;
        }
    }

    private void OnTenantNavLinksChanged()
    {
        DockLayoutState.Refresh();
        StateHasChanged();
    }

    private void SyncAiAssistantDockState()
    {
        _syncingAiAssistantState = true;
        try
        {
            SyncPanelState(
                ShellDockPanels.AiAssistantId,
                !_hideChrome && !IsAiWorkspace && AiAssistantState.IsAvailable && AiAssistantState.IsOpen,
                reason: DockLayoutChangeReason.Refresh);
        }
        finally
        {
            _syncingAiAssistantState = false;
        }
    }

    private void SyncPanelState(
        DockPanelId id,
        bool shouldBeOpen,
        DockLayoutChangeReason reason = DockLayoutChangeReason.Refresh)
    {
        var panel = DockLayoutState.GetPanel(id);

        if (panel is null || panel.State.IsOpen == shouldBeOpen)
        {
            return;
        }

        if (shouldBeOpen)
        {
            DockLayoutState.Open(id, reason);
            return;
        }

        DockLayoutState.Close(id, reason);
    }

    private void OnAnnouncementVisibilityChanged(bool isVisible)
    {
        _announcementVisible = isVisible;
        if (_theme is not null)
        {
            _theme = AppearanceThemeService.CreateTheme(GetAppbarHeight());
            StateHasChanged();
        }
    }

    private void OnSupportAccessBannerVisibilityChanged(bool isVisible)
    {
        _supportAccessBannerVisible = isVisible;
        if (_theme is not null)
        {
            _theme = AppearanceThemeService.CreateTheme(GetAppbarHeight());
            StateHasChanged();
        }
    }

    private string GetAppbarHeight()
    {
        var height = NavbarHeightPx
            + (_announcementVisible ? AnnouncementBarHeightPx : 0)
            + (_supportAccessBannerVisible ? SupportAccessBannerHeightPx : 0);
        return $"{height}px";
    }

    public void Dispose()
    {
        _shellDockLayoutAutosaveCts?.Cancel();
        _shellDockLayoutAutosaveCts?.Dispose();
        _shellSelectionAutosaveCts?.Cancel();
        _shellSelectionAutosaveCts?.Dispose();
        NavigationManager.LocationChanged -= OnLocationChanged;
        MainContentAppearanceState.Changed -= OnMainContentAppearanceChanged;
        AiAssistantState.OnChange -= OnAiAssistantStateChanged;
        TenantNavLinksState.OnChange -= OnTenantNavLinksChanged;
        DockLayoutState.Changed -= OnDockLayoutChanged;
        UiShellState.Changed -= OnShellStateChanged;
        AppearanceThemeService.Changed -= OnAppearanceChanged;
        DockLayoutState.Unregister(ShellDockPanels.WorkspaceNavId);
        DockLayoutState.Unregister(ShellDockPanels.AiAssistantId);
        GC.SuppressFinalize(this);
    }
}
