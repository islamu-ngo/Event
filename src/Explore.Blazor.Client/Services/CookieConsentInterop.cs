using Explore.Blazor.Client.Contracts.Interop;
using Microsoft.JSInterop;

namespace Explore.Blazor.Client.Services;

public class CookieConsentInterop : ICookieConsentInterop, IAsyncDisposable
{
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<CookieConsentInterop> _logger;
    private IJSObjectReference? _module;

    public CookieConsentInterop(IJSRuntime jsRuntime, ILogger<CookieConsentInterop> logger)
    {
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    public async Task<string?> ReadConsentAsync(string consentCookieKey)
    {
        try
        {
            var module = await GetModuleAsync();
            return await module.InvokeAsync<string?>("readConsent", consentCookieKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read consent cookie {Key}", consentCookieKey);
            return null;
        }
    }

    public async Task WriteConsentAsync(string consentCookieKey, string value, int lifetimeDays)
    {
        try
        {
            var module = await GetModuleAsync();
            await module.InvokeVoidAsync("writeConsent", consentCookieKey, value, lifetimeDays);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write consent cookie {Key}", consentCookieKey);
        }
    }

    public async Task ClearConsentAsync(string consentCookieKey)
    {
        try
        {
            var module = await GetModuleAsync();
            await module.InvokeVoidAsync("clearConsent", consentCookieKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clear consent cookie {Key}", consentCookieKey);
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
        _module ??= await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "/js/cookie-consent.js");
        return _module;
    }
}
