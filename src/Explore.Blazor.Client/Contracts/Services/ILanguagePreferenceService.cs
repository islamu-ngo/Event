namespace Explore.Blazor.Client.Contracts.Services;

/// <summary>
/// Persists the current user's language selection to the BFF.
/// <para>
/// Implementations validate the submitted code against the compile-time
/// <c>CultureRegistry</c> before making the HTTP call, so unknown codes never
/// reach the server and never set cookies.
/// </para>
/// </summary>
public interface ILanguagePreferenceService
{
    /// <summary>
    /// Submits <paramref name="languageCode"/> to <c>/bff/language</c> and returns true on success.
    /// Returns false for unknown codes (registry miss) and for HTTP failures (logged).
    /// </summary>
    Task<bool> SetLanguageAsync(string languageCode, CancellationToken ct = default);
}
