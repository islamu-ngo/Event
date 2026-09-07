using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Microsoft.JSInterop;

namespace Explore.Blazor.Client.Services.Accessibility;

public sealed class AccessibilityAnnouncerService : IAccessibilityAnnouncerService, IAsyncDisposable
{
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<AccessibilityAnnouncerService> _logger;
    private IJSObjectReference? _module;

    public AccessibilityAnnouncerService(IJSRuntime jsRuntime, ILogger<AccessibilityAnnouncerService> logger)
    {
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    public async Task AnnouncePoliteAsync(string message)
    {
        try
        {
            var module = await GetModuleAsync();
            await module.InvokeVoidAsync("announce", message, "polite");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to announce polite message");
        }
    }

    public async Task AnnounceAssertiveAsync(string message)
    {
        try
        {
            var module = await GetModuleAsync();
            await module.InvokeVoidAsync("announce", message, "assertive");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to announce assertive message");
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
