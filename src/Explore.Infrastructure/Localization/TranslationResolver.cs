using System.Collections.Concurrent;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Localization;

/// <summary>
/// Resolves translations through the RuntimeTranslationProvider with caching.
/// <para>
/// Cache-key tuple: <c>Translation:{tenantId}:{languageCode}:{mode}:{key}</c> where
/// <c>mode ∈ {"live","offline"}</c>. Flipping <c>force_offline_mode</c> (or a fallback activation)
/// immediately serves a different cache slot — stale TMS data never leaks into offline-mode responses.
/// </para>
/// </summary>
public class TranslationResolver : ITranslationResolver
{
    private readonly RuntimeTranslationProvider _runtimeProvider;
    private readonly ITranslationConfigResolver _configResolver;
    private readonly ITenantContext _tenantContext;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TranslationResolver> _logger;

    // preloadKey → list of cache keys inserted for that (tenant, language, mode) combination.
    // Stored so InvalidateLanguageAsync can purge every entry without needing IMemoryCache key enumeration.
    private readonly ConcurrentDictionary<string, List<string>> _preloadedKeys = new();

    private static readonly TimeSpan LiveCacheDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan OfflineCacheDuration = TimeSpan.FromHours(24);
    private const string CacheKeyPrefix = "Translation:";
    private const string LiveMode = "live";
    private const string OfflineMode = "offline";

    public TranslationResolver(
        RuntimeTranslationProvider runtimeProvider,
        ITranslationConfigResolver configResolver,
        ITenantContext tenantContext,
        IMemoryCache cache,
        ILogger<TranslationResolver> logger)
    {
        _runtimeProvider = runtimeProvider;
        _configResolver = configResolver;
        _tenantContext = tenantContext;
        _cache = cache;
        _logger = logger;
    }

    public async Task<string> ResolveAsync(string key, string languageCode, CancellationToken ct = default)
    {
        var config = await _configResolver.ResolveAsync(ct);
        var mode = ResolveMode(config);
        var tenantId = _tenantContext.TenantId;
        var normalizedLang = languageCode.ToLowerInvariant();
        var cacheKey = BuildCacheKey(tenantId, normalizedLang, mode, key);

        if (_cache.TryGetValue(cacheKey, out string? cached) && cached is not null)
        {
            return cached;
        }

        await EnsureLanguagePreloadedAsync(tenantId, normalizedLang, mode, ct);

        if (_cache.TryGetValue(cacheKey, out cached) && cached is not null)
        {
            return cached;
        }

        return key;
    }

    public async Task<IDictionary<string, string>> ResolveBatchAsync(IEnumerable<string> keys, string languageCode, CancellationToken ct = default)
    {
        var config = await _configResolver.ResolveAsync(ct);
        var mode = ResolveMode(config);
        var tenantId = _tenantContext.TenantId;
        var normalizedLang = languageCode.ToLowerInvariant();

        await EnsureLanguagePreloadedAsync(tenantId, normalizedLang, mode, ct);

        var result = new Dictionary<string, string>();
        foreach (var key in keys)
        {
            var cacheKey = BuildCacheKey(tenantId, normalizedLang, mode, key);
            result[key] = _cache.TryGetValue(cacheKey, out string? cached) && cached is not null
                ? cached
                : key;
        }

        return result;
    }

    public Task InvalidateLanguageAsync(string languageCode, CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;
        var normalizedLang = languageCode.Trim().ToLowerInvariant();

        var cleared = 0;
        foreach (var mode in new[] { LiveMode, OfflineMode })
        {
            var preloadKey = BuildPreloadKey(tenantId, normalizedLang, mode);
            if (!_preloadedKeys.TryRemove(preloadKey, out var cacheKeys))
                continue;

            foreach (var cacheKey in cacheKeys)
            {
                _cache.Remove(cacheKey);
                cleared++;
            }
        }

        _logger.LogInformation(
            "[LOCALIZATION] Invalidated {Count} cache entries for tenant={TenantId} lang={Language} (both modes)",
            cleared, tenantId, normalizedLang);

        return Task.CompletedTask;
    }

    private async Task EnsureLanguagePreloadedAsync(Guid tenantId, string languageCode, string mode, CancellationToken ct)
    {
        var preloadKey = BuildPreloadKey(tenantId, languageCode, mode);
        if (_preloadedKeys.ContainsKey(preloadKey))
            return;

        try
        {
            var exports = await _runtimeProvider.ExportTranslationsAsync(languageCode, ct);
            var exportList = exports.ToList();

            var cacheDuration = mode == OfflineMode ? OfflineCacheDuration : LiveCacheDuration;
            var insertedKeys = new List<string>(exportList.Count);

            foreach (var export in exportList)
            {
                var cacheKey = BuildCacheKey(tenantId, languageCode, mode, export.KeyName);
                _cache.Set(cacheKey, export.Value, cacheDuration);
                insertedKeys.Add(cacheKey);
            }

            _preloadedKeys.TryAdd(preloadKey, insertedKeys);
            _logger.LogInformation(
                "Preloaded {Count} translations for tenant={TenantId} lang={Language} mode={Mode} (TTL: {TTL})",
                exportList.Count, tenantId, languageCode, mode, cacheDuration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to preload translations for tenant={TenantId} lang={Language} mode={Mode}",
                tenantId, languageCode, mode);
            _preloadedKeys.TryAdd(preloadKey, new List<string>()); // prevent retry storms
        }
    }

    private static string ResolveMode(TranslationConfiguration config) =>
        config.ForceOfflineMode || config.Provider == TranslationManagementProviderEnum.None
            ? OfflineMode
            : LiveMode;

    private static string BuildCacheKey(Guid tenantId, string languageCode, string mode, string key)
        => $"{CacheKeyPrefix}{tenantId}:{languageCode}:{mode}:{key}";

    private static string BuildPreloadKey(Guid tenantId, string languageCode, string mode)
        => $"{tenantId}:{languageCode}:{mode}";
}
