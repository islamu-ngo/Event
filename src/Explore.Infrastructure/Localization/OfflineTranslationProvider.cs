using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Localization;

public class OfflineTranslationProvider : ITranslationManagementProvider, IStaticTranslationBundleReader
{
    private readonly ILogger<OfflineTranslationProvider> _logger;
    private readonly IWebHostEnvironment? _environment;
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _bundles = new();
    private readonly Assembly _bundleAssembly;
    private readonly string _bundlePrefix;
    private bool _languagesScanned;
    private readonly List<string> _availableLanguages = [];
    private readonly object _scanLock = new();

    public OfflineTranslationProvider(ILogger<OfflineTranslationProvider> logger)
        : this(logger, environment: null)
    {
    }

    public OfflineTranslationProvider(
        ILogger<OfflineTranslationProvider> logger,
        IWebHostEnvironment? environment)
    {
        _logger = logger;
        _environment = environment;
        _bundleAssembly = typeof(OfflineTranslationProvider).Assembly;
        _bundlePrefix = $"{_bundleAssembly.GetName().Name}.Localization.Bundles.";
    }

    public Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        EnsureLanguagesScanned();
        return Task.FromResult(_availableLanguages.Count > 0);
    }

    public Task ImportKeysAsync(IEnumerable<TranslationKeyImport> keys, CancellationToken ct = default)
    {
        _logger.LogWarning("OfflineTranslationProvider does not support import — bundles are read-only");
        return Task.CompletedTask;
    }

    public Task<IEnumerable<TranslationExport>> ExportTranslationsAsync(string languageCode, CancellationToken ct = default)
    {
        var bundle = GetOrLoadBundle(languageCode);
        var exports = bundle.Select(kvp => new TranslationExport(kvp.Key, kvp.Value));
        return Task.FromResult(exports);
    }

    public Task<IReadOnlyDictionary<string, string>> ReadBundleAsync(
        string languageCode,
        CancellationToken cancellationToken = default) => Task.FromResult(GetOrLoadBundle(languageCode));

    public Task<IEnumerable<string>> GetAvailableLanguagesAsync(CancellationToken ct = default)
    {
        EnsureLanguagesScanned();
        return Task.FromResult<IEnumerable<string>>(_availableLanguages);
    }

    /// <summary>
    /// Clears the in-memory cache entry for a single language. Used by the admin "export" flow so a
    /// freshly-persisted bundle is picked up on the next read without a process restart.
    /// </summary>
    public void InvalidateLanguage(string languageCode)
    {
        var normalised = languageCode.Trim().ToLowerInvariant();
        if (_bundles.TryRemove(normalised, out _))
        {
            _logger.LogDebug("localization_offline_cache_invalidated");
        }
    }

    private IReadOnlyDictionary<string, string> GetOrLoadBundle(string languageCode)
    {
        var normalizedCode = languageCode.Trim().ToLowerInvariant();
        return _bundles.GetOrAdd(normalizedCode, LoadBundle);
    }

    private IReadOnlyDictionary<string, string> LoadBundle(string languageCode)
    {
        var embedded = LoadEmbeddedBundle(languageCode);
        var writable = LoadWritableBundle(languageCode);
        if (writable is null)
        {
            return embedded;
        }

        var merged = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in embedded)
        {
            merged[key] = value;
        }

        foreach (var (key, value) in writable)
        {
            merged[key] = value;
        }

        _logger.LogInformation(
            "localization_offline_bundle_merged embedded_count={EmbeddedCount} writable_count={WritableCount} merged_count={MergedCount}",
            embedded.Count, writable.Count, merged.Count);

        return merged;
    }

    private IReadOnlyDictionary<string, string>? LoadWritableBundle(string languageCode)
    {
        if (_environment is null)
        {
            return null;
        }

        var writablePath = Path.Combine(
            _environment.ContentRootPath,
            "App_Data",
            "Localization",
            "Bundles",
            $"{languageCode}.json");

        if (!File.Exists(writablePath))
        {
            return null;
        }

        try
        {
            using var fileStream = File.OpenRead(writablePath);
            var dict = BundleSchema.Read(fileStream);
            _logger.LogInformation(
                "localization_writable_bundle_loaded count={Count}",
                dict.Count);
            return dict;
        }
        catch (Exception)
        {
            _logger.LogWarning("localization_writable_bundle_read_failed fallback=embedded");
            return null;
        }
    }

    private IReadOnlyDictionary<string, string> LoadEmbeddedBundle(string languageCode)
    {
        var resourceName = $"{_bundlePrefix}{languageCode}.json";
        using var stream = _bundleAssembly.GetManifestResourceStream(resourceName);

        if (stream is null)
        {
            _logger.LogDebug("localization_embedded_bundle_unavailable");
            return new SortedDictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            var dict = BundleSchema.Read(stream);
            _logger.LogInformation("localization_embedded_bundle_loaded count={Count}", dict.Count);
            return dict;
        }
        catch (JsonException)
        {
            _logger.LogError("localization_embedded_bundle_invalid");
            return new Dictionary<string, string>();
        }
    }

    private void EnsureLanguagesScanned()
    {
        if (_languagesScanned) return;

        lock (_scanLock)
        {
            if (_languagesScanned) return;

            var resourceNames = _bundleAssembly.GetManifestResourceNames();
            foreach (var name in resourceNames)
            {
                if (name.StartsWith(_bundlePrefix, StringComparison.OrdinalIgnoreCase) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    var langCode = name[_bundlePrefix.Length..^5];
                    _availableLanguages.Add(langCode);
                }
            }

            _languagesScanned = true;
            _logger.LogInformation("localization_offline_bundles_scanned count={Count}",
                _availableLanguages.Count);
        }
    }
}
