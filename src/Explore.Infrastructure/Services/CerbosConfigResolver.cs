using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Models;
using Explore.Application.Settings;
using Explore.Application.Utilities;
using Explore.Domain.Constants;
using Explore.Domain.Secrets;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Resolves Cerbos PDP settings from the cascading settings engine (SystemSetting → TenantSetting).
/// <para>
/// SaaS scenarios supported:
/// - Instance admin locks Cerbos settings → all tenants use the instance PDP
/// - Instance admin enables customization → tenants can bring their own Cerbos PDP
/// - Default: all tenants use instance PDP with scope-based policy isolation
/// </para>
/// </summary>
public class CerbosConfigResolver : ICerbosConfigResolver
{
    private readonly IHierarchicalSettingsResolver _resolver;
    private readonly ITenantContext _tenantContext;
    private readonly IMemoryCache _cache;
    private readonly CerbosConfigCacheRegistry _cacheRegistry;
    private readonly ICerbosClientFactory _clientFactory;
    private readonly CerbosSettings _instanceSettings;
    private readonly ILogger<CerbosConfigResolver> _logger;
    private readonly ISecretResolver _secretResolver;

    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(5);
    private const string CacheKeyPrefix = "CerbosConfig:";

    public CerbosConfigResolver(
        IHierarchicalSettingsResolver resolver,
        ITenantContext tenantContext,
        IMemoryCache cache,
        CerbosConfigCacheRegistry cacheRegistry,
        ICerbosClientFactory clientFactory,
        IOptions<CerbosSettings> instanceSettings,
        ISecretResolver secretResolver,
        ILogger<CerbosConfigResolver> logger)
    {
        _resolver = resolver;
        _tenantContext = tenantContext;
        _cache = cache;
        _cacheRegistry = cacheRegistry;
        _clientFactory = clientFactory;
        _instanceSettings = instanceSettings.Value;
        _secretResolver = secretResolver;
        _logger = logger;
    }

    public async Task<CerbosConfiguration?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;
        var cacheKey = $"{CacheKeyPrefix}{tenantId}";

        if (_cache.TryGetValue(cacheKey, out CerbosConfiguration? cached))
            return cached;

        var config = await ResolveFromSettingsAsync(tenantId, cancellationToken);

        if (config is not null)
        {
            _cache.Set(cacheKey, config, CacheExpiration);
            if (_cacheRegistry.Track(tenantId, cacheKey, config.Endpoint, out var replacedEndpoint))
                _clientFactory.Evict(replacedEndpoint);
        }

        return config;
    }

    public void InvalidateCache(Guid? tenantId = null)
    {
        if (tenantId.HasValue)
        {
            var cacheKey = $"{CacheKeyPrefix}{tenantId.Value}";
            _cache.Remove(cacheKey);

            if (_cacheRegistry.Untrack(tenantId.Value, out var endpoint))
                _clientFactory.Evict(endpoint);

            _logger.LogInformation("Cerbos config cache invalidated for tenant {TenantId}", tenantId.Value);
        }
        else
        {
            foreach (var entry in _cacheRegistry.UntrackAll())
            {
                _cache.Remove(entry.CacheKey);
                _clientFactory.Evict(entry.Endpoint);
            }

            _logger.LogInformation("Cerbos config cache invalidation requested for all tenants");
        }
    }

    private async Task<CerbosConfiguration?> ResolveFromSettingsAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        // Check if tenant customization is enabled at instance level.
        // When disabled (or locked), all tenants use the instance PDP.
        var ctx = new SettingContext(TenantId: tenantId);

        var customizationEnabled = await _resolver.ResolveAsync<bool>(
            GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled, ctx, cancellationToken);

        if (!customizationEnabled)
            return BuildInstanceDefault();

        // Resolve per-tenant Cerbos mode
        var modeStr = await _resolver.ResolveAsync<string>(
            GovernanceSettingKeys.Cerbos.Mode, ctx, cancellationToken);

        var mode = ParseCerbosMode(modeStr);

        if (mode == CerbosMode.Instance)
            return BuildInstanceDefault();

        // BYO: resolve custom endpoint
        var customEndpoint = await _resolver.ResolveAsync<string>(
            GovernanceSettingKeys.Cerbos.CustomEndpoint, ctx, cancellationToken);

        // Resolve optional Admin API config even when PDP endpoint is blank so package sync can use
        // an explicitly configured Admin API target while runtime authorization still fails closed.
        var adminEndpoint = await _resolver.ResolveAsync<string>(
            GovernanceSettingKeys.Cerbos.CustomAdminEndpoint, ctx, cancellationToken);
        var adminUsername = await ResolveOptionalSecretAsync(
            SecretDefinitionRegistry.Keys.Cerbos.CustomAdminUsername,
            tenantId,
            cancellationToken);
        var adminPassword = await ResolveOptionalSecretAsync(
            SecretDefinitionRegistry.Keys.Cerbos.CustomAdminPassword,
            tenantId,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(customEndpoint))
        {
            _logger.LogWarning(
                "Tenant {TenantId} has cerbos.mode=custom_endpoint but no custom endpoint configured. Runtime authorization fails closed for this tenant",
                tenantId);

            return new CerbosConfiguration
            {
                Endpoint = string.Empty,
                Mode = CerbosMode.CustomEndpoint,
                AdminEndpoint = string.IsNullOrWhiteSpace(adminEndpoint)
                    ? null
                    : GrpcEndpointNormalizer.Normalize(adminEndpoint),
                AdminUsername = string.IsNullOrWhiteSpace(adminUsername) ? null : adminUsername,
                AdminPassword = string.IsNullOrWhiteSpace(adminPassword) ? null : adminPassword,
                IsInstanceDefault = false
            };
        }

        _logger.LogDebug("Resolved BYO Cerbos for tenant {TenantId}", tenantId);

        return new CerbosConfiguration
        {
            Endpoint = GrpcEndpointNormalizer.Normalize(customEndpoint),
            Mode = CerbosMode.CustomEndpoint,
            AdminEndpoint = string.IsNullOrWhiteSpace(adminEndpoint)
                ? null
                : GrpcEndpointNormalizer.Normalize(adminEndpoint),
            AdminUsername = string.IsNullOrWhiteSpace(adminUsername) ? null : adminUsername,
            AdminPassword = string.IsNullOrWhiteSpace(adminPassword) ? null : adminPassword,
            IsInstanceDefault = false
        };
    }

    private CerbosConfiguration BuildInstanceDefault()
    {
        return new CerbosConfiguration
        {
            Endpoint = GrpcEndpointNormalizer.Normalize(_instanceSettings.GrpcEndpoint),
            Mode = CerbosMode.Instance,
            IsInstanceDefault = true
        };
    }

    private async Task<string?> ResolveOptionalSecretAsync(
        string settingKey,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        SecretResolutionResult result = await _secretResolver.ResolveAsync(
            settingKey,
            tenantId,
            cancellationToken);
        return result.Status switch
        {
            SecretResolutionStatus.Resolved => result.Value,
            SecretResolutionStatus.Unconfigured => null,
            _ => throw new InvalidOperationException("cerbos_admin_secret_unavailable")
        };
    }

    private static CerbosMode ParseCerbosMode(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "custom_endpoint" => CerbosMode.CustomEndpoint,
            _ => CerbosMode.Instance
        };

}

public sealed class CerbosConfigCacheRegistry
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, CacheEntry> _entries = new();

    public bool Track(Guid tenantId, string cacheKey, string endpoint, out string replacedEndpoint)
    {
        while (true)
        {
            if (!_entries.TryGetValue(tenantId, out var existing))
            {
                if (_entries.TryAdd(tenantId, new CacheEntry(cacheKey, endpoint)))
                {
                    replacedEndpoint = string.Empty;
                    return false;
                }

                continue;
            }

            var replacement = new CacheEntry(cacheKey, endpoint);
            if (!_entries.TryUpdate(tenantId, replacement, existing))
                continue;

            if (!string.Equals(existing.Endpoint, endpoint, StringComparison.OrdinalIgnoreCase))
            {
                replacedEndpoint = existing.Endpoint;
                return true;
            }

            replacedEndpoint = string.Empty;
            return false;
        }
    }

    public bool Untrack(Guid tenantId, out string endpoint)
    {
        if (_entries.TryRemove(tenantId, out var entry))
        {
            endpoint = entry.Endpoint;
            return true;
        }

        endpoint = string.Empty;
        return false;
    }

    public IReadOnlyList<CacheEntry> UntrackAll()
    {
        var entries = _entries.Values.ToList();
        _entries.Clear();
        return entries;
    }

    public sealed record CacheEntry(string CacheKey, string Endpoint);
}
