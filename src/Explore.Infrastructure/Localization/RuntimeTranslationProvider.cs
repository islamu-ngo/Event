using System.Diagnostics;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Telemetry;
using Explore.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Localization;

/// <summary>
/// Runtime-switchable TMS provider that delegates to the configured provider.
/// Falls back to OfflineTranslationProvider on errors (graceful degradation).
/// </summary>
public sealed class RuntimeTranslationProvider : ITranslationManagementProvider
{
    private readonly TolgeeTranslationProvider _tolgeeProvider;
    private readonly WeblateTranslationProvider _weblateProvider;
    private readonly OfflineTranslationProvider _offlineProvider;
    private readonly NullTranslationProvider _nullProvider;
    private readonly ITranslationConfigResolver _configResolver;
    private readonly TranslationMetrics _metrics;
    private readonly ILogger<RuntimeTranslationProvider> _logger;

    public RuntimeTranslationProvider(
        TolgeeTranslationProvider tolgeeProvider,
        WeblateTranslationProvider weblateProvider,
        OfflineTranslationProvider offlineProvider,
        NullTranslationProvider nullProvider,
        ITranslationConfigResolver configResolver,
        TranslationMetrics metrics,
        ILogger<RuntimeTranslationProvider> logger)
    {
        _tolgeeProvider = tolgeeProvider;
        _weblateProvider = weblateProvider;
        _offlineProvider = offlineProvider;
        _nullProvider = nullProvider;
        _configResolver = configResolver;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        var provider = await ResolveProviderAsync(ct);
        var providerName = provider.GetType().Name;
        try
        {
            var isConnected = await provider.TestConnectionAsync(ct);
            _metrics.RecordConnectionTest(providerName, isConnected ? "success" : "failure");
            return isConnected;
        }
        catch (Exception ex)
        {
            _metrics.RecordConnectionTest(providerName, "error");
            _metrics.RecordFallbackActivated(providerName, ClassifyException(ex));
            _logger.LogError(ex, "[LOCALIZATION] TMS TestConnection failed on {Provider}; falling back to OfflineProvider", providerName);
            return await _offlineProvider.TestConnectionAsync(ct);
        }
    }

    public async Task ImportKeysAsync(IEnumerable<TranslationKeyImport> keys, CancellationToken ct = default)
    {
        var provider = await ResolveProviderAsync(ct);
        try
        {
            await provider.ImportKeysAsync(keys, ct);
        }
        catch (Exception ex)
        {
            _metrics.RecordFallbackActivated(provider.GetType().Name, ClassifyException(ex));
            _logger.LogWarning(ex, "TMS ImportKeys failed on {Provider}", provider.GetType().Name);
        }
    }

    public async Task<IEnumerable<TranslationExport>> ExportTranslationsAsync(string languageCode, CancellationToken ct = default)
    {
        var provider = await ResolveProviderAsync(ct);
        var providerName = provider.GetType().Name;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var results = await provider.ExportTranslationsAsync(languageCode, ct);
            var list = results.ToList();
            if (provider != _offlineProvider)
            {
                var merged = (await _offlineProvider.ExportTranslationsAsync(languageCode, ct))
                    .ToDictionary(value => value.KeyName, value => value, StringComparer.Ordinal);
                foreach (TranslationExport translation in list)
                {
                    merged[translation.KeyName] = translation;
                }
                list = merged.Values.OrderBy(value => value.KeyName, StringComparer.Ordinal).ToList();
            }
            stopwatch.Stop();
            _metrics.RecordFetch(providerName, languageCode, provider == _offlineProvider ? "hit_offline" : "hit_tms");
            _metrics.RecordFetchDuration(providerName, languageCode, stopwatch.Elapsed.TotalSeconds);
            return list;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metrics.RecordFetch(providerName, languageCode, "error");
            _metrics.RecordFetchDuration(providerName, languageCode, stopwatch.Elapsed.TotalSeconds);
            _metrics.RecordFallbackActivated(providerName, ClassifyException(ex));
            _logger.LogError(ex, "[LOCALIZATION] TMS ExportTranslations failed on {Provider} for {Language}; falling back to OfflineProvider",
                providerName, languageCode);

            var fallbackStopwatch = Stopwatch.StartNew();
            var fallbackResults = await _offlineProvider.ExportTranslationsAsync(languageCode, ct);
            var fallbackList = fallbackResults.ToList();
            fallbackStopwatch.Stop();
            _metrics.RecordFetch(nameof(OfflineTranslationProvider), languageCode, "hit_offline");
            _metrics.RecordFetchDuration(nameof(OfflineTranslationProvider), languageCode, fallbackStopwatch.Elapsed.TotalSeconds);
            return fallbackList;
        }
    }

    public async Task<IEnumerable<string>> GetAvailableLanguagesAsync(CancellationToken ct = default)
    {
        var provider = await ResolveProviderAsync(ct);
        try
        {
            return await provider.GetAvailableLanguagesAsync(ct);
        }
        catch (Exception ex)
        {
            _metrics.RecordFallbackActivated(provider.GetType().Name, ClassifyException(ex));
            _logger.LogError(ex, "[LOCALIZATION] TMS GetAvailableLanguages failed on {Provider}; falling back to OfflineProvider", provider.GetType().Name);
            return await _offlineProvider.GetAvailableLanguagesAsync(ct);
        }
    }

    private async Task<ITranslationManagementProvider> ResolveProviderAsync(CancellationToken ct)
    {
        try
        {
            var config = await _configResolver.ResolveAsync(ct);

            if (config.ForceOfflineMode)
            {
                _logger.LogWarning(
                    "[LOCALIZATION] force_offline_mode active; bypassing configured provider {Provider} and serving offline bundles",
                    config.Provider);
                return _offlineProvider;
            }

            return config.Provider switch
            {
                TranslationManagementProviderEnum.Tolgee => _tolgeeProvider,
                TranslationManagementProviderEnum.Weblate => _weblateProvider,
                TranslationManagementProviderEnum.None => _offlineProvider,
                _ => _offlineProvider
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve TMS provider; defaulting to OfflineProvider");
            return _offlineProvider;
        }
    }

    private static string ClassifyException(Exception ex) => ex switch
    {
        TaskCanceledException or OperationCanceledException => "timeout",
        HttpRequestException hre when hre.StatusCode == System.Net.HttpStatusCode.Unauthorized
            || hre.StatusCode == System.Net.HttpStatusCode.Forbidden => "auth_error",
        HttpRequestException hre when hre.StatusCode == System.Net.HttpStatusCode.NotFound => "not_found",
        HttpRequestException hre when hre.StatusCode == System.Net.HttpStatusCode.TooManyRequests => "rate_limited",
        HttpRequestException => "network_error",
        _ => "other"
    };
}
