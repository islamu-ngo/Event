using System.Security.Claims;
using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Shell;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.Events;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Services.Docking;
using Explore.Blazor.Client.Services.Shell;
using Explore.Blazor.Client.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;

namespace Explore.Blazor.Client.Layout;

public partial class NavMenu : IDisposable
{
    [Inject]
    protected NavigationManager Nav { get; set; } = null!;

    [Inject]
    protected AuthenticationStateProvider AuthStateProvider { get; set; } = null!;

    [Inject]
    protected IUserService UserService { get; set; } = null!;

    [Inject]
    protected IPublicExperienceService PublicExperienceService { get; set; } = null!;

    [Inject]
    protected IUserSettingsService UserSettingsService { get; set; } = null!;

    [Inject]
    protected IEventCreationEligibilityService EventCreationEligibilityService { get; set; } = null!;

    [Inject]
    protected IUiShellContextService ShellContextService { get; set; } = null!;

    [Inject]
    protected IDialogService DialogService { get; set; } = null!;

    [Inject]
    protected CurrentUserState CurrentUserState { get; set; } = null!;

    [Inject]
    private IAccessibilityFocusService AccessibilityFocusService { get; set; } = default!;

    [Inject]
    protected AiAssistantState AiAssistantState { get; set; } = null!;

    [Inject]
    protected DockLayoutState DockLayoutState { get; set; } = null!;

    [Inject]
    protected IWorkspaceRegistry WorkspaceRegistry { get; set; } = null!;

    [Inject]
    protected UiShellState UiShellState { get; set; } = null!;

    private bool _dropdownOpen = false;
    private bool _isSearchOverlayOpen = false;
    private UserDto? _currentUser;
    private bool _userLoaded = false;
    private string _brandDisplayName = string.Empty;
    private string _brandLogoUrl = string.Empty;
    private string _eventCatalogLabel = "events";
    public string SearchQuery { get; set; } = "";
    private MudTextField<string> _searchField = null!;
    private MudTextField<string> _overlaySearchField = null!;
    private EventCreationEligibility _eventCreationEligibility = EventCreationEligibility.NotEligible;
    private bool _isSingleTenantMode = true;
    private bool _isCurrentUserInstanceAdmin;
    private bool _isCurrentUserTenantAdmin;
    private bool _showAddEventForAnonymous;
    private bool _languagePickerEnabled = true;
    private IReadOnlyList<SettingsScopeDto> _organizationScopes = [];
    private IReadOnlyList<SettingsScopeDto> _groupScopes = [];
    private IReadOnlyList<ManagedActorDto> _organizationActors = [];
    private IReadOnlyList<ManagedActorDto> _groupActors = [];
    private bool _orgSubmenuOpen;
    private bool _groupSubmenuOpen;
    private const string AiAssistantPreferencesCategory = "AiAssistantPreferences";
    private const string ShowAiAssistantNavbarButtonKey = "ai_assistant_preferences.show_navbar_button";

    private bool IsStudioWorkspace => UiShellState.ActiveWorkspace == WorkspaceKey.Studio;
    private bool ShowGlobalSearch => UiShellState.ActiveWorkspace == WorkspaceKey.Events
        || UiShellState.ActiveWorkspace == WorkspaceKey.Studio;
    private bool ShowEventPrimaryAction => _eventCreationEligibility.CanCreate && ShowGlobalSearch;
    private bool ShowAnonymousEventAction => _showAddEventForAnonymous
        && UiShellState.ActiveWorkspace == WorkspaceKey.Events;
    private string SearchPlaceholder => IsStudioWorkspace
        ? "Search managed events…  ⌘K"
        : $"Search {_eventCatalogLabel}…  ⌘K";
    private string PrimaryActionLabel => IsStudioWorkspace ? "Create" : "Add Event";
    private string PrimaryActionRoute => IsStudioWorkspace
        ? "/events/create"
        : _eventCreationEligibility.CreateEventRoute;
    private string? ActingActorName => IsStudioWorkspace
        ? UiShellState.ActiveActor?.DisplayName
        : null;
    private string? ProfileAdminSettingsHref => _isCurrentUserInstanceAdmin
        ? "/settings/instance"
        : _isCurrentUserTenantAdmin
            ? "/settings/admin"
            : null;

    protected override async Task OnInitializedAsync()
    {
        AiAssistantState.OnChange += StateHasChanged;
        DockLayoutState.Changed += OnDockLayoutChanged;
        UiShellState.Changed += StateHasChanged;
        CurrentUserState.OnChanged += OnCurrentUserChanged;
        await LoadPublicExperienceAsync();
        await LoadCurrentUserAsync();
        await LoadEventCreationEligibilityAsync();
        await LoadShellContextAsync();
    }

    private void HandleSearchKeyPress(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                Nav.NavigateTo($"{SearchRoute}?q={Uri.EscapeDataString(SearchQuery)}");
            }
            else
            {
                Nav.NavigateTo(SearchRoute);
            }

            // Close search overlay after submitting on mobile/tablet
            if (_isSearchOverlayOpen)
            {
                _isSearchOverlayOpen = false;
            }
        }
        else if (e.Key == "Escape" && _isSearchOverlayOpen)
        {
            CloseSearchOverlay();
        }
    }

    private void OpenSearchOverlay()
    {
        _isSearchOverlayOpen = true;
        // Auto-focus the overlay search input after Blazor re-renders
        _ = InvokeAsync(async () =>
        {
            await Task.Yield(); // Let Blazor render the overlay
            await _overlaySearchField.FocusAsync();
        });
    }

    private void CloseSearchOverlay()
    {
        _isSearchOverlayOpen = false;
        SearchQuery = string.Empty;
    }

    /// <summary>
    /// Checks if the brand display name is exactly two space-separated words.
    /// If so, outputs the first word as <paramref name="top"/> and second as <paramref name="bottom"/>
    /// so the template can render them stacked vertically on intermediate viewport widths.
    /// Returns false when the brand name is empty, a single word, or has more than two parts.
    /// </summary>
    private bool CanStackBrandName(out string top, out string bottom)
    {
        top = string.Empty;
        bottom = string.Empty;

        if (string.IsNullOrWhiteSpace(_brandDisplayName))
            return false;

        var parts = _brandDisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            top = parts[0];
            bottom = parts[1];
            return true;
        }

        return false;
    }

    private string SearchRoute => IsStudioWorkspace ? "/studio/events" : "/events";

    private async Task LoadPublicExperienceAsync()
    {
        var authState = await AuthStateProvider.GetAuthenticationStateAsync();
        var isAuthenticated = authState.User.Identity?.IsAuthenticated == true;
        var shellTask = PublicExperienceService.GetCachedShellAsync();
        var shell = shellTask is null ? null : await shellTask;
        if (shell is not null)
        {
            if (!string.IsNullOrWhiteSpace(shell.Home?.BrandDisplayName))
            {
                _brandDisplayName = shell.Home.BrandDisplayName;
            }

            _brandLogoUrl = shell.Home?.BrandLogoUrl ?? string.Empty;
            _eventCatalogLabel = string.IsNullOrWhiteSpace(shell.EventCatalog?.Label)
                ? _eventCatalogLabel
                : shell.EventCatalog.Label.ToLowerInvariant();
        }

        var settingsTask = PublicExperienceService.GetSettingsAsync();
        var settings = settingsTask is null ? null : await settingsTask;
        if (settings == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_brandDisplayName) && !string.IsNullOrWhiteSpace(settings.BrandDisplayName))
        {
            _brandDisplayName = settings.BrandDisplayName;
        }

        _brandLogoUrl = string.IsNullOrWhiteSpace(_brandLogoUrl)
            ? settings.BrandLogoUrl ?? string.Empty
            : _brandLogoUrl;
        AiAssistantState.SetPolicy(
            settings.IsAiAssistantEnabled == true,
            settings.IsAiAssistantAvailable == true,
            settings.AiAssistantAllowAnonymousAccess == true,
            isAuthenticated);
        _languagePickerEnabled = settings.ClientPickerEnabled ?? true;

        if (isAuthenticated)
        {
            await LoadAiAssistantPreferenceAsync();
        }

        // Show "Add Event" button to anonymous visitors when at least one
        // submission type is enabled, prompting them to log in on click.
        _showAddEventForAnonymous = settings.AllowUserSubmittedEvents == true
            || settings.AllowOrganizationSubmittedEvents == true
            || settings.AllowGroupSubmittedEvents == true;
    }

    private async Task LoadAiAssistantPreferenceAsync()
    {
        try
        {
            var response = await UserSettingsService.GetSettingsAsync(AiAssistantPreferencesCategory);
            var setting = response?.Settings?.FirstOrDefault(s => s.Key == ShowAiAssistantNavbarButtonKey);
            AiAssistantState.SetUserNavbarPreference(ParseBooleanSetting(setting?.Value, defaultValue: true));
        }
        catch
        {
            AiAssistantState.SetUserNavbarPreference(true);
        }
    }

    private static bool ParseBooleanSetting(string? value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        try
        {
            return JsonSerializer.Deserialize<bool>(value);
        }
        catch (JsonException)
        {
            return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
        }
    }

    private async Task LoadCurrentUserAsync()
    {
        if (_userLoaded) return;

        try
        {
            var authState = await AuthStateProvider.GetAuthenticationStateAsync();
            if (authState.User.Identity?.IsAuthenticated == true)
            {
                _currentUser = await UserService.GetCurrentUserAsync();
                _userLoaded = true;
            }
        }
        catch
        {
            // Silently fail - will fall back to initials
        }
    }

    private async Task ToggleDropdown()
    {
        if (!_dropdownOpen)
        {
            await LoadShellContextAsync();
        }

        _dropdownOpen = !_dropdownOpen;
        if (!_dropdownOpen)
        {
            _orgSubmenuOpen = false;
            _groupSubmenuOpen = false;
        }
    }

    private void ToggleSidebarPanel()
    {
        DockLayoutState.Toggle(ShellDockPanels.WorkspaceNavId);
    }

    private void ToggleAiAssistantPanel()
    {
        AiAssistantState.Toggle();
        MirrorShellDockPanel(ShellDockPanels.AiAssistantId, AiAssistantState.IsAvailable && AiAssistantState.IsOpen);
    }

    private void MirrorShellDockPanel(DockPanelId id, bool shouldBeOpen)
    {
        var panel = DockLayoutState.GetPanel(id);
        if (panel is null || panel.State.IsOpen == shouldBeOpen)
        {
            return;
        }

        if (shouldBeOpen)
        {
            DockLayoutState.Open(id);
            return;
        }

        DockLayoutState.Close(id);
    }

    private void CloseDropdown()
    {
        _dropdownOpen = false;
        _orgSubmenuOpen = false;
        _groupSubmenuOpen = false;
    }

    private void NavigateToPersonalSettings(Microsoft.AspNetCore.Components.Web.MouseEventArgs args)
    {
        CloseDropdown();
        UiShellState.NavigateToPersonalSettings("/settings/personal", args);
    }

    private bool IsSidebarDockOpen => DockLayoutState.GetPanel(ShellDockPanels.WorkspaceNavId)?.State.IsOpen == true;

    private bool HasWorkspaceNavigation => DockLayoutState.GetPanel(ShellDockPanels.WorkspaceNavId) is not null
        && WorkspaceRegistry.Workspaces.Any(workspace =>
            workspace.Key == UiShellState.ActiveWorkspace && workspace.NavigationProviderType is not null);

    private bool IsLoginRoute =>
        Nav.ToBaseRelativePath(Nav.Uri)
            .Split('?', '#')[0]
            .TrimEnd('/')
            .Equals("login", StringComparison.OrdinalIgnoreCase);

    private void OnDockLayoutChanged()
    {
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnCurrentUserChanged()
    {
        _userLoaded = false;
        _ = InvokeAsync(async () =>
        {
            await LoadCurrentUserAsync();
            await LoadShellContextAsync();
            await InvokeAsync(StateHasChanged);
        });
    }

    private void ToggleOrgSubmenu()
    {
        _orgSubmenuOpen = !_orgSubmenuOpen;
        if (_orgSubmenuOpen) _groupSubmenuOpen = false;
    }

    private void ToggleGroupSubmenu()
    {
        _groupSubmenuOpen = !_groupSubmenuOpen;
        if (_groupSubmenuOpen) _orgSubmenuOpen = false;
    }

    private string GetInitials(string? name)
    {
        return DisplayHelper.GetInitials(name);
    }

    private bool HasAnyAdminAuthority(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
            return false;

        return _isCurrentUserInstanceAdmin
               || _isCurrentUserTenantAdmin
               || _organizationScopes.Count > 0
               || _groupScopes.Count > 0;
    }

    private async Task LoadEventCreationEligibilityAsync()
    {
        try
        {
            var authState = await AuthStateProvider.GetAuthenticationStateAsync();
            if (authState.User.Identity?.IsAuthenticated != true)
            {
                return;
            }

            _eventCreationEligibility = await EventCreationEligibilityService.GetEligibilityAsync();
        }
        catch
        {
            // Silently fail - button simply won't appear
        }
    }

    private async Task LoadShellContextAsync()
    {
        try
        {
            var authState = await AuthStateProvider.GetAuthenticationStateAsync();
            if (authState.User.Identity?.IsAuthenticated != true)
            {
                _isSingleTenantMode = true;
                _isCurrentUserInstanceAdmin = false;
                _isCurrentUserTenantAdmin = false;
                _organizationScopes = [];
                _groupScopes = [];
                _organizationActors = [];
                _groupActors = [];
                return;
            }

            var context = await ShellContextService.GetCachedContextAsync();
            if (context is null)
            {
                _isSingleTenantMode = true;
                _isCurrentUserInstanceAdmin = false;
                _isCurrentUserTenantAdmin = false;
                _organizationScopes = [];
                _groupScopes = [];
                _organizationActors = [];
                _groupActors = [];
                return;
            }

            _isSingleTenantMode = string.IsNullOrWhiteSpace(context.DeploymentMode)
                || string.Equals(context.DeploymentMode, "SingleTenant", StringComparison.OrdinalIgnoreCase);

            var scopes = context.SettingsScopes ?? [];
            _isCurrentUserInstanceAdmin = scopes.Any(s => string.Equals(s.Scope, "Instance", StringComparison.OrdinalIgnoreCase));
            _isCurrentUserTenantAdmin = scopes.Any(s => string.Equals(s.Scope, "Tenant", StringComparison.OrdinalIgnoreCase));
            _organizationScopes = scopes
                .Where(s => string.Equals(s.Scope, "Organization", StringComparison.OrdinalIgnoreCase))
                .ToList();
            _groupScopes = scopes
                .Where(s => string.Equals(s.Scope, "Group", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var actors = context.ManagedActors ?? [];
            _organizationActors = actors
                .Where(actor => string.Equals(actor.ActorType, "Organization", StringComparison.OrdinalIgnoreCase))
                .ToList();
            _groupActors = actors
                .Where(actor => string.Equals(actor.ActorType, "Group", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch
        {
            _isSingleTenantMode = true;
            _isCurrentUserInstanceAdmin = false;
            _isCurrentUserTenantAdmin = false;
            _organizationScopes = [];
            _groupScopes = [];
            _organizationActors = [];
            _groupActors = [];
        }
    }

    private void StartLogin()
    {
        var returnUrl = new Uri(Nav.Uri).PathAndQuery;
        Nav.NavigateTo($"/auth/login?returnUrl={Uri.EscapeDataString(returnUrl)}", forceLoad: true);
    }

    private async Task OpenLoginPrompt(string? returnUrl, string? message = null)
    {
        returnUrl ??= new Uri(Nav.Uri).PathAndQuery;
        await AccessibilityFocusService.SaveFocusAsync();
        await LoginPromptDialog.ShowAsync(DialogService, returnUrl, message);
        await AccessibilityFocusService.RestoreFocusAsync();
    }

    private async Task FocusSearchAsync()
    {
        await _searchField.FocusAsync();
    }

    public void Dispose()
    {
        AiAssistantState.OnChange -= StateHasChanged;
        DockLayoutState.Changed -= OnDockLayoutChanged;
        UiShellState.Changed -= StateHasChanged;
        CurrentUserState.OnChanged -= OnCurrentUserChanged;
        GC.SuppressFinalize(this);
    }
}
