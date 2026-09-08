using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;

namespace Explore.Blazor.Client.Components.Shell;

public partial class TenantNavBottomSheet : ComponentBase, IAsyncDisposable
{
    private const string BodyElementId = "body";
    private const string ScrollLockClass = "scroll-locked";

    [Inject] private NavigationManager NavigationManager { get; set; } = null!;
    [Inject] private IAccessibilityFocusService AccessibilityFocusService { get; set; } = null!;
    [Inject] private IScrollManager ScrollManager { get; set; } = null!;

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public IReadOnlyList<TenantNavigationLinkDto> Links { get; set; } = [];
    [Parameter] public EventCallback OnClose { get; set; }

    private bool _lifecycleActive;
    private bool _focusSaved;
    private bool _scrollLocked;
    private bool _disposed;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (IsOpen && !_lifecycleActive)
        {
            _lifecycleActive = true;
            await AccessibilityFocusService.SaveFocusAsync();
            _focusSaved = true;
            if (!IsOpen || _disposed)
            {
                await DeactivateAsync();
                return;
            }

            await ScrollManager.LockScrollAsync(BodyElementId, ScrollLockClass);
            _scrollLocked = true;
            if (!IsOpen || _disposed)
            {
                await DeactivateAsync();
                return;
            }

            await AccessibilityFocusService.FocusByIdAsync("tenant-nav-bottom-sheet-close", preventScroll: true);
        }
        else if (!IsOpen && _lifecycleActive)
        {
            await DeactivateAsync();
        }
    }

    private async Task HandleKeyDownAsync(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            await CloseAsync();
        }
    }

    private async Task HandleLinkClickAsync(TenantNavigationLinkDto link)
    {
        await CloseAsync();

        if (link.OpenInNewTab != true && !string.IsNullOrWhiteSpace(link.Url))
        {
            NavigationManager.NavigateTo(link.Url);
        }
    }

    private Task CloseAsync() => OnClose.InvokeAsync();

    private bool IsActive(TenantNavigationLinkDto link)
    {
        if (link.OpenInNewTab == true || string.IsNullOrWhiteSpace(link.Url))
        {
            return false;
        }

        try
        {
            var current = new Uri(NavigationManager.Uri);
            var target = NavigationManager.ToAbsoluteUri(link.Url);
            if (!string.Equals(current.GetLeftPart(UriPartial.Authority), target.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var targetPath = target.AbsolutePath.TrimEnd('/');
            var currentPath = current.AbsolutePath.TrimEnd('/');
            return targetPath.Length == 0
                ? currentPath.Length == 0
                : string.Equals(currentPath, targetPath, StringComparison.OrdinalIgnoreCase)
                  || currentPath.StartsWith($"{targetPath}/", StringComparison.OrdinalIgnoreCase);
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    private static string GetLinkClass(bool isActive) =>
        $"tenant-nav-bottom-sheet__link{(isActive ? " tenant-nav-bottom-sheet__link--active" : string.Empty)}";

    private async Task DeactivateAsync()
    {
        if (_scrollLocked)
        {
            _scrollLocked = false;
            await ScrollManager.UnlockScrollAsync(BodyElementId, ScrollLockClass);
        }

        if (_focusSaved)
        {
            _focusSaved = false;
            await AccessibilityFocusService.RestoreFocusAsync("[data-testid='mobile-links-tab']");
        }

        _lifecycleActive = false;
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await DeactivateAsync();
        GC.SuppressFinalize(this);
    }
}
