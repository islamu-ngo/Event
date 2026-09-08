using Explore.Blazor.Client.Contracts.Interop;
using Microsoft.JSInterop;

namespace Explore.Blazor.Client.Services;

public sealed class WebPushBrowserInterop(
    IJSRuntime jsRuntime,
    ILogger<WebPushBrowserInterop> logger) : IWebPushBrowserInterop
{
    private const string ModulePath = "/js/web-push.js";
    private IJSObjectReference? module;

    public async Task<WebPushBrowserState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await (await GetModuleAsync(cancellationToken)).InvokeAsync<WebPushBrowserState>(
                "getWebPushState",
                cancellationToken);
        }
        catch (Exception ex) when (IsExpectedInteropFailure(ex))
        {
            logger.LogDebug(ex, "Web Push browser state is unavailable");
            return new WebPushBrowserState(false, "unsupported", false, string.Empty);
        }
    }

    public async Task<WebPushBrowserSubscription?> SubscribeAsync(
        string applicationServerKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(applicationServerKey))
        {
            return null;
        }

        try
        {
            return await (await GetModuleAsync(cancellationToken)).InvokeAsync<WebPushBrowserSubscription?>(
                "subscribeWebPush",
                cancellationToken,
                applicationServerKey);
        }
        catch (Exception ex) when (IsExpectedInteropFailure(ex))
        {
            logger.LogDebug(ex, "Web Push browser subscription failed");
            return null;
        }
    }

    public async Task<bool> UnsubscribeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await (await GetModuleAsync(cancellationToken)).InvokeAsync<bool>(
                "unsubscribeWebPush",
                cancellationToken);
        }
        catch (Exception ex) when (IsExpectedInteropFailure(ex))
        {
            logger.LogDebug(ex, "Web Push browser unsubscribe failed");
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (module is null)
        {
            return;
        }

        try
        {
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }
    }

    private async Task<IJSObjectReference> GetModuleAsync(CancellationToken cancellationToken)
    {
        module ??= await jsRuntime.InvokeAsync<IJSObjectReference>("import", cancellationToken, ModulePath);
        return module;
    }

    private static bool IsExpectedInteropFailure(Exception ex) =>
        ex is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException;
}
