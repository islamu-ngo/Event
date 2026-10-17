using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Models;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Domain.Secrets;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Storage;

/// <summary>
/// Resolves S3 governance through the hierarchical settings engine and credentials through
/// the selected external secret authority.
/// </summary>
public class S3ConfigResolver : IS3ConfigResolver
{
    private readonly IHierarchicalSettingsReader _resolver;
    private readonly ITenantContext _tenantContext;
    private readonly IMemoryCache _cache;
    private readonly S3ConfigCacheRegistry _cacheRegistry;
    private readonly ISecretResolver _secretResolver;
    private readonly ILogger<S3ConfigResolver> _logger;

    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(5);
    private const string CacheKeyPrefix = "S3Config:";

    public S3ConfigResolver(
        IHierarchicalSettingsReader resolver,
        ITenantContext tenantContext,
        IMemoryCache cache,
        S3ConfigCacheRegistry cacheRegistry,
        ISecretResolver secretResolver,
        ILogger<S3ConfigResolver> logger)
    {
        _resolver = resolver;
        _tenantContext = tenantContext;
        _cache = cache;
        _cacheRegistry = cacheRegistry;
        _secretResolver = secretResolver;
        _logger = logger;
    }

    public async Task<S3Configuration?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;
        var cacheKey = $"{CacheKeyPrefix}{tenantId}";

        if (_cacheRegistry.TryGet(_cache, cacheKey, out var cached, out long generation))
        {
            return cached;
        }

        var config = await ResolveFromSettingsAsync(tenantId, cancellationToken);

        if (config is not null)
        {
            _cacheRegistry.Store(_cache, cacheKey, config, generation, CacheExpiration);
        }

        return config;
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default)
        => await ResolveAsync(cancellationToken) is not null;

    public void InvalidateCache(Guid? tenantId = null)
    {
        if (tenantId.HasValue)
        {
            _cacheRegistry.Invalidate(_cache, $"{CacheKeyPrefix}{tenantId.Value}");
        }
        else
        {
            _cacheRegistry.Invalidate(_cache, null);
            _logger.LogInformation("S3 config cache invalidated for all tenants");
        }
    }

    private async Task<S3Configuration?> ResolveFromSettingsAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var endpoint = await ResolveStringAsync(
            GovernanceSettingKeys.Storage.Endpoint,
            tenantId,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            _logger.LogDebug("S3 not configured because the endpoint is empty");
            return null;
        }

        var bucketName = await ResolveStringAsync(
            GovernanceSettingKeys.Storage.BucketName,
            tenantId,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(bucketName))
        {
            _logger.LogDebug("S3 not configured because the bucket is empty");
            return null;
        }

        var accessKeyId = await ResolveSecretAsync(
            SecretDefinitionRegistry.Keys.Storage.AccessKeyId,
            tenantId,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(accessKeyId))
        {
            _logger.LogDebug("S3 not configured because the access key is unavailable");
            return null;
        }

        var secretAccessKey = await ResolveSecretAsync(
            SecretDefinitionRegistry.Keys.Storage.SecretAccessKey,
            tenantId,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(secretAccessKey))
        {
            _logger.LogDebug("S3 not configured because the secret key is unavailable");
            return null;
        }

        var uploadExpiration = await _resolver.ResolveAsync<int>(GovernanceSettingKeys.Storage.UploadUrlExpirationMinutes, new SettingContext(TenantId: tenantId), cancellationToken);
        var forcePathStyle = await _resolver.ResolveAsync<bool>(GovernanceSettingKeys.Storage.ForcePathStyle, new SettingContext(TenantId: tenantId), cancellationToken);

        var region = await ResolveStringAsync(
            GovernanceSettingKeys.Storage.Region,
            tenantId,
            cancellationToken);
        var publicEndpoint = await ResolveStringAsync(
            GovernanceSettingKeys.Storage.PublicEndpoint,
            tenantId,
            cancellationToken);

        return new S3Configuration
        {
            Endpoint = endpoint,
            BucketName = bucketName,
            AccessKeyId = accessKeyId,
            SecretAccessKey = secretAccessKey,
            Region = string.IsNullOrWhiteSpace(region) ? "us-east-1" : region,
            PublicEndpoint = publicEndpoint,
            ForcePathStyle = forcePathStyle,
            UploadUrlExpirationMinutes = uploadExpiration > 0 ? uploadExpiration : 60,
            DownloadUrlExpirationMinutes = 60
        };
    }

    private async Task<string?> ResolveStringAsync(
        string settingKey,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        await _resolver.ResolveAsync<string>(
            settingKey,
            new SettingContext(TenantId: tenantId),
            cancellationToken);

    private async Task<string?> ResolveSecretAsync(
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
            _ => throw new InvalidOperationException("storage_secret_unavailable")
        };
    }
}

/// <summary>Shares cache ownership and invalidation generations across scoped resolvers.</summary>
public sealed class S3ConfigCacheRegistry
{
    private readonly object _gate = new();
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private long _generation;

    internal bool TryGet(IMemoryCache cache, string key, out S3Configuration? configuration, out long generation)
    {
        lock (_gate)
        {
            generation = _generation;
            return cache.TryGetValue(key, out configuration);
        }
    }

    internal void Store(IMemoryCache cache, string key, S3Configuration configuration,
        long generation, TimeSpan expiration)
    {
        lock (_gate)
        {
            if (generation != _generation) return;
            _keys.Add(key);
            cache.Set(key, configuration, expiration);
        }
    }

    internal void Invalidate(IMemoryCache cache, string? key)
    {
        lock (_gate)
        {
            _generation++;
            if (key is not null)
            {
                _keys.Remove(key);
                cache.Remove(key);
                return;
            }
            foreach (string cachedKey in _keys) cache.Remove(cachedKey);
            _keys.Clear();
        }
    }
}
