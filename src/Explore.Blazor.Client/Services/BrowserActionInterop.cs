using Explore.Blazor.Client.Contracts.Interop;
using Microsoft.JSInterop;

namespace Explore.Blazor.Client.Services;

public sealed class BrowserActionInterop : IBrowserActionInterop, IAsyncDisposable
{
    private const string ModulePath = "/js/browser-actions.js";

    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<BrowserActionInterop> _logger;
    private IJSObjectReference? _module;

    public BrowserActionInterop(IJSRuntime jsRuntime, ILogger<BrowserActionInterop> logger)
    {
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    public async Task<bool> ShareAsync(string title, string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        try
        {
            var module = await GetModuleAsync(cancellationToken);
            return await module.InvokeAsync<bool>(
                "share",
                cancellationToken,
                string.IsNullOrWhiteSpace(title) ? "Event" : title,
                url);
        }
        catch (Exception ex) when (IsExpectedBrowserInteropFailure(ex))
        {
            _logger.LogDebug(ex, "Browser share action was unavailable.");
            return false;
        }
    }

    public async Task<bool> CopyTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            var module = await GetModuleAsync(cancellationToken);
            return await module.InvokeAsync<bool>("copyText", cancellationToken, text);
        }
        catch (Exception ex) when (IsExpectedBrowserInteropFailure(ex))
        {
            _logger.LogDebug(ex, "Browser clipboard action was unavailable.");
            return false;
        }
    }

    public async Task<bool> ScrollToElementByIdAsync(
        string elementId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(elementId))
        {
            return false;
        }

        try
        {
            var module = await GetModuleAsync(cancellationToken);
            return await module.InvokeAsync<bool>("scrollToElementById", cancellationToken, elementId);
        }
        catch (Exception ex) when (IsExpectedBrowserInteropFailure(ex))
        {
            _logger.LogDebug(ex, "Browser scroll action was unavailable.");
            return false;
        }
    }

    public async Task<bool> DownloadBase64FileAsync(
        string base64Content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(base64Content) || string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        try
        {
            var module = await GetModuleAsync(cancellationToken);
            return await module.InvokeAsync<bool>(
                "downloadBase64File",
                cancellationToken,
                base64Content,
                fileName,
                string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        }
        catch (Exception ex) when (IsExpectedBrowserInteropFailure(ex))
        {
            _logger.LogDebug(ex, "Browser file download action was unavailable.");
            return false;
        }
    }

    public async Task<bool> DownloadFileFromUrlAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        try
        {
            var module = await GetModuleAsync(cancellationToken);
            return await module.InvokeAsync<bool>("downloadFileFromUrl", cancellationToken, url);
        }
        catch (Exception ex) when (IsExpectedBrowserInteropFailure(ex))
        {
            _logger.LogDebug(ex, "Browser URL download action was unavailable.");
            return false;
        }
    }

    public async Task<bool> OpenSameOriginNewTabAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        try
        {
            var module = await GetModuleAsync(cancellationToken);
            return await module.InvokeAsync<bool>("openSameOriginNewTab", cancellationToken, url);
        }
        catch (Exception ex) when (IsExpectedBrowserInteropFailure(ex))
        {
            _logger.LogDebug(ex, "Browser new-tab action was unavailable.");
            return false;
        }
    }

    public async Task<bool> FocusOpenerAndCloseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var module = await GetModuleAsync(cancellationToken);
            return await module.InvokeAsync<bool>("focusOpenerAndClose", cancellationToken);
        }
        catch (Exception ex) when (IsExpectedBrowserInteropFailure(ex))
        {
            _logger.LogDebug(ex, "Original registration tab could not be focused.");
            return false;
        }
    }


    public async ValueTask DisposeAsync()
    {
        if (_module is null)
        {
            return;
        }

        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }
    }

    private static bool IsExpectedBrowserInteropFailure(Exception ex)
        => ex is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException;

    private async Task<IJSObjectReference> GetModuleAsync(CancellationToken cancellationToken)
    {
        _module ??= await _jsRuntime.InvokeAsync<IJSObjectReference>("import", cancellationToken, ModulePath);
        return _module;
    }
}
