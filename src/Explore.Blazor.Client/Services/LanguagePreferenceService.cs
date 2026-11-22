using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Localization;
using Explore.Blazor.Client.Services.Interop;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services;

/// <summary>
/// Default <see cref="ILanguagePreferenceService"/> implementation.
/// </summary>
public sealed class LanguagePreferenceService : ILanguagePreferenceService
{
    private readonly ILanguagePreferenceCookieInterop _cookieInterop;
    private readonly ILogger<LanguagePreferenceService> _logger;

    public LanguagePreferenceService(
        ILanguagePreferenceCookieInterop cookieInterop,
        ILogger<LanguagePreferenceService> logger)
    {
        _cookieInterop = cookieInterop;
        _logger = logger;
    }

    public async Task<bool> SetLanguageAsync(string languageCode, CancellationToken ct = default)
    {
        if (!CultureRegistry.TryGetEntry(languageCode, out var entry))
        {
            _logger.LogWarning(
                "[LOCALIZATION] Rejected language preference '{Code}'; not in CultureRegistry",
                languageCode);
            return false;
        }

        try
        {
            return await _cookieInterop.PersistLanguagePreferenceAsync(entry.Code, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LOCALIZATION] Failed to persist language preference for code {Code}", entry.Code);
            return false;
        }
    }
}
