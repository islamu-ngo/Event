using Explore.Application.Contracts.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Localization;

public class NullTranslationProvider : ITranslationManagementProvider
{
    private readonly ILogger<NullTranslationProvider> _logger;

    public NullTranslationProvider(ILogger<NullTranslationProvider> logger)
    {
        _logger = logger;
    }

    public Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("NullTranslationProvider: TestConnection always returns false");
        return Task.FromResult(false);
    }

    public Task ImportKeysAsync(IEnumerable<TranslationKeyImport> keys, CancellationToken ct = default)
    {
        _logger.LogDebug("NullTranslationProvider: ImportKeys is a no-op");
        return Task.CompletedTask;
    }

    public Task<IEnumerable<TranslationExport>> ExportTranslationsAsync(string languageCode, CancellationToken ct = default)
    {
        _logger.LogDebug("NullTranslationProvider: ExportTranslations returning empty for {Language}", languageCode);
        return Task.FromResult(Enumerable.Empty<TranslationExport>());
    }

    public Task<IEnumerable<string>> GetAvailableLanguagesAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("NullTranslationProvider: GetAvailableLanguages returning empty");
        return Task.FromResult(Enumerable.Empty<string>());
    }
}
