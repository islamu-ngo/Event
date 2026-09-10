using Explore.Blazor.Client.Contracts.Interop;
using Explore.Blazor.Client.Models.Analytics;
using Microsoft.JSInterop;

namespace Explore.Blazor.Client.Services;

public class AnalyticsInterop : IAnalyticsInterop, IAsyncDisposable
{
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<AnalyticsInterop> _logger;
    private IJSObjectReference? _module;

    public AnalyticsInterop(IJSRuntime jsRuntime, ILogger<AnalyticsInterop> logger)
    {
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    public async Task InitAsync(string analyticsProvider, bool analyticsEnabled, string analyticsConsentMode, string analyticsTransportMode, bool allowIdentify, string? apiKey, string? endpointUrl, PosthogClientBootstrapModel? posthogOptions = null)
    {
        try
        {
            var module = await GetModuleAsync();
            object? jsPosthogOptions = posthogOptions is not null
                ? new
                {
                    cookielessMode = posthogOptions.CookielessMode,
                    personProfiles = posthogOptions.PersonProfiles,
                    sessionReplay = posthogOptions.SessionReplay,
                    autocapture = posthogOptions.Autocapture,
                    heatmaps = posthogOptions.Heatmaps,
                    toolbar = posthogOptions.Toolbar
                }
                : null;
            await module.InvokeVoidAsync("initAnalytics", analyticsProvider, analyticsEnabled, analyticsConsentMode, analyticsTransportMode, allowIdentify, apiKey ?? string.Empty, endpointUrl ?? string.Empty, jsPosthogOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Analytics bridge initialization failed: {ExceptionType}", ex.GetType().Name);
        }
    }

    public async Task TrackAsync(string eventName, IDictionary<string, object>? properties = null)
    {
        try
        {
            var module = await GetModuleAsync();
            await module.InvokeVoidAsync("trackEvent", eventName, properties ?? new Dictionary<string, object>());
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Analytics bridge track failed: {ExceptionType}", ex.GetType().Name);
        }
    }

    public async Task IdentifyAsync(string distinctId, IDictionary<string, object>? traits = null)
    {
        try
        {
            var module = await GetModuleAsync();
            await module.InvokeVoidAsync("identifyUser", distinctId, traits ?? new Dictionary<string, object>());
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Analytics bridge identify failed: {ExceptionType}", ex.GetType().Name);
        }
    }

    public async Task PageViewAsync(string pagePath, IDictionary<string, object>? properties = null)
    {
        try
        {
            var module = await GetModuleAsync();
            await module.InvokeVoidAsync("trackPageView", pagePath, properties ?? new Dictionary<string, object>());
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Analytics bridge page view failed: {ExceptionType}", ex.GetType().Name);
        }
    }

    public async Task OptInCapturingAsync()
    {
        try
        {
            var module = await GetModuleAsync();
            await module.InvokeVoidAsync("optInCapturing");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Analytics bridge opt-in failed: {ExceptionType}", ex.GetType().Name);
        }
    }

    public async Task OptOutCapturingAsync()
    {
        try
        {
            var module = await GetModuleAsync();
            await module.InvokeVoidAsync("optOutCapturing");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Analytics bridge opt-out failed: {ExceptionType}", ex.GetType().Name);
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
        _module ??= await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "/js/analytics-bridge.js");
        return _module;
    }
}
