using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Microsoft.JSInterop;

namespace Explore.Blazor.Client.Services.Accessibility;

public sealed class AccessibilityFocusService : IAccessibilityFocusService, IAsyncDisposable
{
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<AccessibilityFocusService> _logger;
    private IJSObjectReference? _module;

    public AccessibilityFocusService(IJSRuntime jsRuntime, ILogger<AccessibilityFocusService> logger)
    {
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    public async Task FocusAsync(string cssSelector, bool preventScroll = false)
    {
        try
        {
            var module = await GetModuleAsync();
            await module.InvokeVoidAsync("setFocus", cssSelector, preventScroll);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set focus on selector {Selector}", cssSelector);
        }
    }

    public async Task FocusByIdAsync(string elementId, bool preventScroll = false)
    {
        try
        {
            var module = await GetModuleAsync();
            await module.InvokeVoidAsync("setFocusById", elementId, preventScroll);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set focus on element {ElementId}", elementId);
        }
    }

    public async Task FocusMainContentAsync()
    {
        await FocusByIdAsync("main-content");
    }

    public async Task FocusOnNavigateAsync()
    {
        try
        {
            var module = await GetModuleAsync();
            await module.InvokeVoidAsync("focusOnNavigate");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to focus on navigate");
        }
    }

    public async Task SaveFocusAsync()
    {
        try
        {
            var module = await GetModuleAsync();
            await module.InvokeVoidAsync("saveActiveElement");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save active element");
        }
    }

    public async Task RestoreFocusAsync(string? fallbackSelector = null)
    {
        try
        {
            var module = await GetModuleAsync();
            await module.InvokeVoidAsync("restoreFocus", fallbackSelector);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to restore focus");
        }
    }

    public async Task<string> GetPreferredMotionAsync()
    {
        try
        {
            var module = await GetModuleAsync();
            return await module.InvokeAsync<string>("getPreferredMotion");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get preferred motion");
            return "no-preference";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }

    private async Task<IJSObjectReference> GetModuleAsync()
    {
        _module ??= await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "/js/accessibility.js");
        return _module;
    }
}
