namespace Explore.Blazor.Client.Contracts.Interop;

public interface ICookieConsentInterop
{
    Task<string?> ReadConsentAsync(string consentCookieKey);
    Task WriteConsentAsync(string consentCookieKey, string value, int lifetimeDays);
    Task ClearConsentAsync(string consentCookieKey);
}
