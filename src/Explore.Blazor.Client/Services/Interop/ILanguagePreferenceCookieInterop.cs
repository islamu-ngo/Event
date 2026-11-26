namespace Explore.Blazor.Client.Services.Interop;

/// <summary>
/// Submits the language preference from the browser so it receives the BFF's culture cookies.
/// </summary>
public interface ILanguagePreferenceCookieInterop
{
    ValueTask<bool> PersistLanguagePreferenceAsync(string languageCode, CancellationToken cancellationToken);
}
