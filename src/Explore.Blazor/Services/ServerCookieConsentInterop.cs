using Explore.Blazor.Client.Contracts.Interop;

namespace Explore.Blazor.Services;

public sealed class ServerCookieConsentInterop : ICookieConsentInterop
{
    public Task<string?> ReadConsentAsync(string consentCookieKey) => Task.FromResult<string?>(null);

    public Task WriteConsentAsync(string consentCookieKey, string value, int lifetimeDays) => Task.CompletedTask;

    public Task ClearConsentAsync(string consentCookieKey) => Task.CompletedTask;
}
