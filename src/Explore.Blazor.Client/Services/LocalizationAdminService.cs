using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;

namespace Explore.Blazor.Client.Services;

public sealed class LocalizationAdminService : ILocalizationAdminService
{
    private readonly ILocalizationAdminClient _api;
    private readonly ILogger<LocalizationAdminService> _logger;

    public LocalizationAdminService(
        ILocalizationAdminClient api,
        ILogger<LocalizationAdminService> logger)
    {
        _api = api;
        _logger = logger;
    }

    public async Task<LocalizationConfigDto?> GetConfigurationAsync(CancellationToken ct = default)
    {
        try
        {
            return await _api.GetLocalizationConfigurationAsync(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LOCALIZATION ADMIN] Failed to fetch configuration");
            return null;
        }
    }

    public async Task<LocalizationAdminCommandResult> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            await _api.TestLocalizationTmsConnectionAsync(cancellationToken: ct);
            return new LocalizationAdminCommandResult(true, "TMS connection OK.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LOCALIZATION ADMIN] Test connection failed");
            return new LocalizationAdminCommandResult(false, "Test connection failed: " + ex.Message);
        }
    }

    public async Task<LocalizationAdminCommandResult> ExportFromTmsAsync(string languageCode, CancellationToken ct = default)
    {
        try
        {
            await _api.ExportLocalizationFromTmsAsync(languageCode, cancellationToken: ct);
            return new LocalizationAdminCommandResult(true, $"Exported translations for '{languageCode}'.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LOCALIZATION ADMIN] Export failed for {Language}", languageCode);
            return new LocalizationAdminCommandResult(false, "Export failed: " + ex.Message);
        }
    }

    public async Task<IReadOnlyDictionary<string, string>?> ExportBundleAsync(string languageCode, CancellationToken ct = default)
    {
        try
        {
            var response = await _api.ExportLocalizationBundleAsync(languageCode, cancellationToken: ct);
            return response as IReadOnlyDictionary<string, string>
                ?? response.ToDictionary(pair => pair.Key, pair => pair.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LOCALIZATION ADMIN] Static bundle export failed for {Language}", languageCode);
            return null;
        }
    }

    public async Task<LocalizationAdminCommandResult> ImportBundleAsync(
        string languageCode,
        IReadOnlyDictionary<string, string> translations,
        CancellationToken ct = default)
    {
        try
        {
            var request = new ImportLocalizationBundleDto
            {
                LanguageCode = languageCode,
                Translations = translations.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            };

            var response = await _api.ImportLocalizationBundleAsync(request, cancellationToken: ct);
            return MapCommandResult(response, $"Imported static bundle for '{languageCode}'.", $"Static bundle import failed for '{languageCode}'.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LOCALIZATION ADMIN] Static bundle import failed for {Language}", languageCode);
            return new LocalizationAdminCommandResult(false, "Static bundle import failed: " + ex.Message);
        }
    }

    public async Task<LocalizationAdminCommandResult> UpdateGovernanceAsync(UpdateLocalizationGovernanceDto payload, CancellationToken ct = default)
    {
        try
        {
            var response = await _api.UpdateLocalizationGovernanceAsync(payload, cancellationToken: ct);
            return MapCommandResult(response, "Localization governance saved.", "Localization governance save failed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LOCALIZATION ADMIN] Governance update failed");
            return new LocalizationAdminCommandResult(false, "Governance update failed: " + ex.Message);
        }
    }

    public async Task<WritablePathHealth?> GetBundlePathHealthAsync(CancellationToken ct = default)
    {
        try
        {
            return await _api.CheckLocalizationBundleHealthAsync(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LOCALIZATION ADMIN] Failed to fetch bundle path health");
            return null;
        }
    }

    private static LocalizationAdminCommandResult MapCommandResult(
        BaseCommandResponseOfGuid response,
        string successFallback,
        string failureFallback)
    {
        bool success = response.Success == true;
        return new LocalizationAdminCommandResult(
            success,
            string.IsNullOrWhiteSpace(response.Message)
                ? success ? successFallback : failureFallback
                : response.Message);
    }
}
