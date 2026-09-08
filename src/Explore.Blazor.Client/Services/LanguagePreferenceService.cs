using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Localization;
using Microsoft.Extensions.Logging;
using Refit;

namespace Explore.Blazor.Client.Services;

public interface ILanguagePreferenceApi
{
    [Post("/bff/language")]
    Task<IApiResponse> SetLanguageAsync([Query] string lang, CancellationToken cancellationToken);
}

/// <summary>
/// Default <see cref="ILanguagePreferenceService"/> implementation.
/// </summary>
public sealed class LanguagePreferenceService : ILanguagePreferenceService
{
    private readonly ILanguagePreferenceApi _api;
    private readonly ILogger<LanguagePreferenceService> _logger;

    public LanguagePreferenceService(ILanguagePreferenceApi api, ILogger<LanguagePreferenceService> logger)
    {
        _api = api;
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
            var response = await _api.SetLanguageAsync(entry.Code, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[LOCALIZATION] /bff/language returned {Status} for code {Code}",
                    (int)response.StatusCode, entry.Code);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LOCALIZATION] Failed to persist language preference for code {Code}", entry.Code);
            return false;
        }
    }
}
