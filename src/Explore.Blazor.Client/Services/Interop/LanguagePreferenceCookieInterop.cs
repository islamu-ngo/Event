using Microsoft.JSInterop;

namespace Explore.Blazor.Client.Services.Interop;

internal sealed class LanguagePreferenceCookieInterop(IJSRuntime jsRuntime) : ILanguagePreferenceCookieInterop
{
    public async ValueTask<bool> PersistLanguagePreferenceAsync(
        string languageCode,
        CancellationToken cancellationToken)
    {
        await using var module = await jsRuntime.InvokeAsync<IJSObjectReference>(
            "import", cancellationToken, "/js/bff.js");
        return await module.InvokeAsync<bool>("persistLanguagePreference", cancellationToken, languageCode);
    }
}
