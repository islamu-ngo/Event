namespace Explore.Secrets.Services;

using System.Collections.Frozen;
using System.Collections.Concurrent;
using System.Diagnostics;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Explore.Secrets.Observability;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Resolves a <c>settingKey</c> to its current <see cref="ResolvedSecret"/> value.
/// </summary>
/// <remarks>
/// <para><b>Core algorithm (no-fallback):</b></para>
/// <list type="number">
///   <item>Look up the winning <see cref="SecretBinding"/> in the hierarchy:
///         Tenant (if tenantId supplied) -> Instance -> registry-owned instance default.</item>
///   <item>Dispatch to the <see cref="ISecretSource"/> matching
///         <see cref="SecretBinding.SourceType"/>.</item>
///   <item>If the source returns <c>null</c>, return <c>null</c>. Do NOT fall back
///         to another source type. This is the architectural invariant that kills
///         the old Infisical->DB->AppSetting precedence chain.</item>
/// </list>
/// <para>Results are cached in-process for <see cref="CacheTtl"/>. Writes to the
/// binding invalidate the cache via <see cref="InvalidateAsync"/>.</para>
/// </remarks>
public sealed class SecretResolver : ISecretResolver
{
    /// <summary>In-memory cache TTL (resolved values live here, not the binding itself).</summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly ISecretBindingRepository _bindings;
    private readonly FrozenDictionary<SecretSourceType, ISecretSource> _sources;
    private readonly IMemoryCache _cache;
    private readonly SecretResolverMetrics _metrics;
    private readonly ILogger<SecretResolver> _logger;
    private readonly SecretProviderType _provider;
    private readonly string _infisicalEnvironment;
    private readonly ConcurrentDictionary<string, string> _cacheKeys = new(StringComparer.Ordinal);

    public SecretResolver(
        ISecretBindingRepository bindings,
        IEnumerable<ISecretSource> sources,
        IMemoryCache cache,
        SecretResolverMetrics metrics,
        ILogger<SecretResolver> logger,
        IOptions<SecretProviderOptions> options)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);

        _bindings = bindings;
        _cache = cache;
        _metrics = metrics;
        _logger = logger;
        _provider = options.Value.Provider;
        _infisicalEnvironment = options.Value.Infisical.Environment;

        // Index sources by SourceType. Duplicate types = bug => throw at startup.
        _sources = sources.ToFrozenDictionary(s => s.SourceType);
    }

    /// <inheritdoc />
    public async Task<SecretResolutionResult> ResolveAsync(
        string settingKey,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingKey);

        var stopwatch = Stopwatch.StartNew();
        var status = SecretResolutionStatus.Unavailable;
        try
        {
            // 1) Find the winning binding in the hierarchy.
            var binding = await ResolveBindingAsync(settingKey, tenantId, cancellationToken)
                .ConfigureAwait(false);

            if (binding is null)
            {
                status = SecretResolutionStatus.Unconfigured;
                _metrics.RecordMiss(source: null);
                return SecretResolutionResult.Unconfigured;
            }

            var result = await ResolveBoundBindingAsync(binding, cancellationToken).ConfigureAwait(false);
            status = result.Status;
            return result;
        }
        finally
        {
            stopwatch.Stop();
            _metrics.RecordDuration(status, stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    public async Task<SecretResolutionResult> ResolveTenantBindingAsync(
        Guid tenantId,
        Guid bindingId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || bindingId == Guid.Empty)
        {
            throw new ArgumentException("Tenant and binding identifiers are required.");
        }

        SecretBinding? binding = await _bindings.GetByTenantAndIdAsync(tenantId, bindingId, cancellationToken).ConfigureAwait(false);
        if (binding is null)
        {
            _metrics.RecordMiss(source: null);
            return SecretResolutionResult.Unconfigured;
        }

        return await ResolveBoundBindingAsync(binding, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SecretResolutionResult> ResolveQualifiedAsync(
        string settingKey,
        SecretScope scope,
        Guid? scopeId,
        string qualifier,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(qualifier);

        var stopwatch = Stopwatch.StartNew();
        var status = SecretResolutionStatus.Unavailable;
        try
        {
            SecretBinding? binding = await _bindings.GetByKeyScopeAndQualifierAsync(
                settingKey,
                scope,
                scopeId,
                qualifier,
                cancellationToken).ConfigureAwait(false);

            if (binding is null)
            {
                status = SecretResolutionStatus.Unconfigured;
                _metrics.RecordMiss(source: null);
                return SecretResolutionResult.Unconfigured;
            }

            var result = await ResolveBoundBindingAsync(binding, cancellationToken).ConfigureAwait(false);
            status = result.Status;
            return result;
        }
        finally
        {
            stopwatch.Stop();
            _metrics.RecordDuration(status, stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    /// <inheritdoc />
    public Task InvalidateAsync(
        string settingKey,
        SecretScope scope,
        Guid? scopeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingKey);
        var prefix = BuildCacheKeyPrefix(settingKey, scope, scopeId);
        foreach (var pair in _cacheKeys)
        {
            if (pair.Key.StartsWith(prefix, StringComparison.Ordinal)
                && _cacheKeys.TryRemove(pair.Key, out var cacheKey))
            {
                _cache.Remove(cacheKey);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Walks Tenant -> Instance -> canonical registry default. A null return means
    /// the key is unknown or unavailable for instance scope.
    /// </summary>
    private async Task<SecretBinding?> ResolveBindingAsync(
        string settingKey,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        if (tenantId.HasValue && tenantId.Value != Guid.Empty)
        {
            var tenantBinding = await _bindings.GetByKeyAndScopeAsync(
                settingKey, SecretScope.Tenant, tenantId.Value, cancellationToken)
                .ConfigureAwait(false);

            if (tenantBinding is not null)
            {
                return tenantBinding;
            }
        }

        var instanceBinding = await _bindings.GetByKeyAndScopeAsync(
            settingKey, SecretScope.Instance, scopeId: null, cancellationToken)
            .ConfigureAwait(false);
        if (instanceBinding is not null)
        {
            return instanceBinding;
        }

        return CreateDefaultInstanceBinding(settingKey);
    }

    private SecretBinding? CreateDefaultInstanceBinding(string settingKey)
    {
        SecretDefinition? definition = SecretDefinitionRegistry.TryGet(settingKey);
        if (definition is null || !definition.AllowedScopes.Contains(SecretScope.Instance))
        {
            return null;
        }

        return _provider switch
        {
            SecretProviderType.Environment or SecretProviderType.UserSecrets => SecretBinding.CreateEnvironmentVariable(
                settingKey,
                SecretScope.Instance,
                scopeId: null,
                definition.DefaultEnvironmentVariableName),
            SecretProviderType.Infisical when !string.IsNullOrWhiteSpace(_infisicalEnvironment) =>
                SecretBinding.CreateInfisical(
                    settingKey,
                    SecretScope.Instance,
                    scopeId: null,
                    _infisicalEnvironment,
                    definition.DefaultInfisicalPath,
                    definition.DefaultInfisicalKey),
            _ => null,
        };
    }

    private async Task<SecretResolutionResult> ResolveBoundBindingAsync(
        SecretBinding binding,
        CancellationToken cancellationToken)
    {
        var selectedSource = SelectedSourceType();
        if (selectedSource != binding.SourceType)
        {
            _logger.LogError("secret_source_invalid");
            _metrics.RecordError(binding.SourceType, SecretResolutionStatus.Invalid);
            return SecretResolutionResult.Invalid;
        }

        var logicalCacheKey = BuildCacheKey(binding.SettingKey, binding.Scope, binding.ScopeId, binding.Qualifier);
        var cacheKey = $"{logicalCacheKey}::{binding.SourceType}::{binding.Id:N}";
        if (_cache.TryGetValue<ResolvedSecret>(cacheKey, out var cached) && cached is not null)
        {
            _metrics.RecordCacheHit();
            return SecretResolutionResult.Resolved(cached);
        }

        _metrics.RecordCacheMiss();
        if (!_sources.TryGetValue(binding.SourceType, out var source))
        {
            _logger.LogError("secret_source_invalid");
            _metrics.RecordError(binding.SourceType, SecretResolutionStatus.Invalid);
            return SecretResolutionResult.Invalid;
        }

        SecretResolutionResult result;
        try
        {
            result = await source.GetSecretAsync(binding, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Do not catch general exception types - source boundary
        catch (Exception)
#pragma warning restore CA1031
        {
            _logger.LogError("secret_source_unavailable");
            _metrics.RecordError(binding.SourceType, SecretResolutionStatus.Unavailable);
            return SecretResolutionResult.Unavailable;
        }

        if (!result.IsResolved)
        {
            if (result.Status == SecretResolutionStatus.Unconfigured)
            {
                _metrics.RecordMiss(binding.SourceType);
            }
            else
            {
                _metrics.RecordError(binding.SourceType, result.Status);
            }

            return result;
        }

        if (_cacheKeys.TryGetValue(logicalCacheKey, out var previousCacheKey)
            && !string.Equals(previousCacheKey, cacheKey, StringComparison.Ordinal))
        {
            _cache.Remove(previousCacheKey);
        }

        _cacheKeys[logicalCacheKey] = cacheKey;
        _cache.Set(cacheKey, result.Secret!, CacheTtl);
        _metrics.RecordSuccess(binding.SourceType);
        return result;
    }

    internal static string BuildCacheKey(string settingKey, SecretScope scope, Guid? scopeId, string qualifier = "") =>
        $"{BuildCacheKeyPrefix(settingKey, scope, scopeId)}{qualifier}";

    private static string BuildCacheKeyPrefix(string settingKey, SecretScope scope, Guid? scopeId) =>
        scopeId.HasValue
            ? $"secret::{settingKey}::{scope}::{scopeId.Value:N}::"
            : $"secret::{settingKey}::{scope}::-::";

    private SecretSourceType? SelectedSourceType() => _provider switch
    {
        SecretProviderType.Environment or SecretProviderType.UserSecrets => SecretSourceType.EnvironmentVariable,
        SecretProviderType.Infisical => SecretSourceType.Infisical,
        _ => null,
    };
}
