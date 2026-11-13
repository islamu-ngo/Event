using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Explore.Blazor.Client.Services;

/// <summary>
/// Default <see cref="ILanguagePreferenceService"/> implementation.
/// </summary>
public sealed class LanguagePreferenceService : ILanguagePreferenceService
{
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<LanguagePreferenceService> _logger;

    public LanguagePreferenceService(IJSRuntime jsRuntime, ILogger<LanguagePreferenceService> logger)
    {
        _jsRuntime = jsRuntime;
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
            await using var module = await _jsRuntime.InvokeAsync<IJSObjectReference>(
                "import", ct, "/js/bff.js");
            return await module.InvokeAsync<bool>("persistLanguagePreference", ct, entry.Code);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LOCALIZATION] Failed to persist language preference for code {Code}", entry.Code);
            return false;
        }
    }
}
