using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Settings;
using Explore.Domain.Common.Localization;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Localization;

/// <summary>
/// Resolves translation settings from the cascading settings engine (SystemSetting → TenantSetting).
/// </summary>
public class TranslationConfigResolver : ITranslationConfigResolver
{
    private readonly IHierarchicalSettingsResolver _resolver;
    private readonly ITenantContext _tenantContext;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TranslationConfigResolver> _logger;

    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(5);
    private const string CacheKeyPrefix = "TranslationConfig:";
    private const string SafeFallbackLanguage = "en";

    public TranslationConfigResolver(
        IHierarchicalSettingsResolver resolver,
        ITenantContext tenantContext,
        IMemoryCache cache,
        ILogger<TranslationConfigResolver> logger)
    {
        _resolver = resolver;
        _tenantContext = tenantContext;
        _cache = cache;
        _logger = logger;
    }

    public async Task<TranslationConfiguration> ResolveAsync(CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;
        var cacheKey = $"{CacheKeyPrefix}{tenantId}";

        if (_cache.TryGetValue(cacheKey, out TranslationConfiguration? cached) && cached is not null)
        {
            return cached;
        }

        var config = await ResolveFromSettingsAsync(tenantId, ct);
        _cache.Set(cacheKey, config, CacheExpiration);
        return config;
    }

    public void InvalidateCache(Guid? tenantId = null)
    {
        if (tenantId.HasValue)
        {
            _cache.Remove($"{CacheKeyPrefix}{tenantId.Value}");
            return;
        }

        _logger.LogInformation("Translation config cache invalidation requested for all tenants");
    }

    private async Task<TranslationConfiguration> ResolveFromSettingsAsync(Guid tenantId, CancellationToken ct)
    {
        var ctx = new SettingContext(TenantId: tenantId);

        var providerStr = await _resolver.ResolveAsync<string>(GovernanceSettingKeys.Localization.TmsProvider, ctx, ct);
        var apiUrl = await _resolver.ResolveAsync<string>(GovernanceSettingKeys.Localization.TmsApiUrl, ctx, ct);
        var projectId = await _resolver.ResolveAsync<string>(GovernanceSettingKeys.Localization.TmsProjectId, ctx, ct);
        var component = await _resolver.ResolveAsync<string>(GovernanceSettingKeys.Localization.TmsComponent, ctx, ct);
        var defaultLanguage = await _resolver.ResolveAsync<string>(GovernanceSettingKeys.Localization.DefaultLanguage, ctx, ct);
        var enabledLanguagesStr = await _resolver.ResolveAsync<string>(GovernanceSettingKeys.Localization.EnabledLanguages, ctx, ct);
        var fallbackLanguageStr = await _resolver.ResolveAsync<string>(GovernanceSettingKeys.Localization.FallbackLanguage, ctx, ct);
        var clientPickerEnabledStr = await _resolver.ResolveAsync<string>(GovernanceSettingKeys.Localization.ClientPickerEnabled, ctx, ct);
        var forceOfflineModeStr = await _resolver.ResolveAsync<string>(GovernanceSettingKeys.Localization.ForceOfflineMode, ctx, ct);

        var provider = ParseProvider(providerStr);
        var resolvedDefaultLanguage = NormaliseOrDefault(defaultLanguage, SafeFallbackLanguage);
        var enabledLanguages = ParseEnabledLanguages(enabledLanguagesStr, resolvedDefaultLanguage);
        var fallbackLanguage = ResolveFallbackLanguage(fallbackLanguageStr, enabledLanguages, resolvedDefaultLanguage);
        var clientPickerEnabled = ParseBool(clientPickerEnabledStr, defaultValue: true);
        var forceOfflineMode = ParseBool(forceOfflineModeStr, defaultValue: false);

        _logger.LogDebug(
            "Translation config resolved for tenant {TenantId}: Provider={Provider}, DefaultLanguage={DefaultLanguage}, Enabled=[{Enabled}], Fallback={Fallback}, PickerEnabled={PickerEnabled}, ForceOffline={ForceOffline}",
            tenantId, provider, resolvedDefaultLanguage, string.Join(",", enabledLanguages), fallbackLanguage, clientPickerEnabled, forceOfflineMode);

        return new TranslationConfiguration(
            Provider: provider,
            ApiUrl: string.IsNullOrWhiteSpace(apiUrl) ? null : apiUrl,
            ProjectId: string.IsNullOrWhiteSpace(projectId) ? null : projectId,
            Component: string.IsNullOrWhiteSpace(component) ? null : component,
            DefaultLanguage: resolvedDefaultLanguage)
        {
            EnabledLanguages = enabledLanguages,
            FallbackLanguage = fallbackLanguage,
            ClientPickerEnabled = clientPickerEnabled,
            ForceOfflineMode = forceOfflineMode
        };
    }

    private static TranslationManagementProviderEnum ParseProvider(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "tolgee" => TranslationManagementProviderEnum.Tolgee,
            "weblate" => TranslationManagementProviderEnum.Weblate,
            _ => TranslationManagementProviderEnum.None
        };
    }

    private string NormaliseOrDefault(string? value, string fallback)
    {
        var normalised = CultureRegistry.Normalize(value);
        if (normalised.Length == 0 || !CultureRegistry.Contains(normalised))
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                _logger.LogWarning(
                    "[LOCALIZATION] Governance value '{Value}' is not in CultureRegistry; falling back to '{Fallback}'",
                    value, fallback);
            }
            return fallback;
        }
        return normalised;
    }

    private IReadOnlyList<string> ParseEnabledLanguages(string? csv, string defaultLanguage)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return new[] { defaultLanguage };
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var raw in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalised = CultureRegistry.Normalize(raw);
            if (normalised.Length == 0 || !CultureRegistry.Contains(normalised))
            {
                _logger.LogWarning(
                    "[LOCALIZATION] enabled_languages entry '{Entry}' is not in CultureRegistry; dropping",
                    raw);
                continue;
            }
            if (seen.Add(normalised))
            {
                result.Add(normalised);
            }
        }

        if (result.Count == 0)
        {
            _logger.LogWarning(
                "[LOCALIZATION] enabled_languages resolved to an empty list; defaulting to [{Default}]",
                defaultLanguage);
            return new[] { defaultLanguage };
        }

        return result;
    }

    private string ResolveFallbackLanguage(string? value, IReadOnlyList<string> enabledLanguages, string defaultLanguage)
    {
        var normalised = CultureRegistry.Normalize(value);
        if (normalised.Length > 0
            && CultureRegistry.Contains(normalised)
            && enabledLanguages.Contains(normalised, StringComparer.OrdinalIgnoreCase))
        {
            return normalised;
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            _logger.LogWarning(
                "[LOCALIZATION] fallback_language '{Value}' is not a valid enabled culture; substituting '{Substitute}'",
                value, defaultLanguage);
        }

        return enabledLanguages.Contains(defaultLanguage, StringComparer.OrdinalIgnoreCase)
            ? defaultLanguage
            : enabledLanguages[0];
    }

    private static bool ParseBool(string? value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }
        return bool.TryParse(value.Trim(), out var parsed) ? parsed : defaultValue;
    }
}
