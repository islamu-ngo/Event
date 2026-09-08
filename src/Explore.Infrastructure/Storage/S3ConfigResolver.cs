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
    private readonly IHierarchicalSettingsResolver _resolver;
    private readonly ITenantContext _tenantContext;
    private readonly IMemoryCache _cache;
    private readonly ISecretResolver _secretResolver;
    private readonly ILogger<S3ConfigResolver> _logger;

    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(5);
    private const string CacheKeyPrefix = "S3Config:";

    public S3ConfigResolver(
        IHierarchicalSettingsResolver resolver,
        ITenantContext tenantContext,
        IMemoryCache cache,
        ISecretResolver secretResolver,
        ILogger<S3ConfigResolver> logger)
    {
        _resolver = resolver;
        _tenantContext = tenantContext;
        _cache = cache;
        _secretResolver = secretResolver;
        _logger = logger;
    }

    public async Task<S3Configuration?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;
        var cacheKey = $"{CacheKeyPrefix}{tenantId}";

        if (_cache.TryGetValue(cacheKey, out S3Configuration? cached))
        {
            return cached;
        }

        var config = await ResolveFromSettingsAsync(tenantId, cancellationToken);

        if (config is not null)
        {
            _cache.Set(cacheKey, config, CacheExpiration);
        }

        return config;
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default)
        => await ResolveAsync(cancellationToken) is not null;

    public void InvalidateCache(Guid? tenantId = null)
    {
        if (tenantId.HasValue)
        {
            _cache.Remove($"{CacheKeyPrefix}{tenantId.Value}");
        }
        else
        {
            // Can't enumerate MemoryCache keys — callers should invalidate specific tenants.
            // For a full flush, the SettingsResolver also has its own cache invalidation.
            _logger.LogInformation("S3 config cache invalidation requested for all tenants");
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
            _logger.LogDebug("S3 not configured for tenant {TenantId}: endpoint is empty", tenantId);
            return null;
        }

        var bucketName = await ResolveStringAsync(
            GovernanceSettingKeys.Storage.BucketName,
            tenantId,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(bucketName))
        {
            _logger.LogDebug("S3 not configured for tenant {TenantId}: bucket_name is empty", tenantId);
            return null;
        }

        var accessKeyId = await ResolveSecretAsync(
            SecretDefinitionRegistry.Keys.Storage.AccessKeyId,
            tenantId,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(accessKeyId))
        {
            _logger.LogDebug("S3 not configured for tenant {TenantId}: access_key_id is empty", tenantId);
            return null;
        }

        var secretAccessKey = await ResolveSecretAsync(
            SecretDefinitionRegistry.Keys.Storage.SecretAccessKey,
            tenantId,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(secretAccessKey))
        {
            _logger.LogDebug("S3 not configured for tenant {TenantId}: secret_access_key is empty", tenantId);
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
