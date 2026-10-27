namespace Explore.Infrastructure.Secrets;

using System.Collections.Frozen;
using System.Diagnostics;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.ServiceDefaults.Secrets;
using Microsoft.Extensions.Logging;

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
public sealed class SecretResolver : ISecretResolver, IRetainedSecretResolver
{
    /// <summary>In-memory cache TTL (resolved values live here, not the binding itself).</summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly ISecretBindingRepository _bindings;
    private readonly FrozenDictionary<SecretSourceType, ISecretSource> _sources;
    private readonly SecretResolverCacheState _cacheState;
    private readonly SecretResolverMetrics _metrics;
    private readonly ILogger<SecretResolver> _logger;
    private readonly SecretAuthorityKind _provider;
    private readonly SecretConsumerRole _consumer;
    private readonly string _infisicalEnvironment;
    private readonly string? _authorityEndpoint;
    private readonly string? _authorityProject;

    public SecretResolver(
        ISecretBindingRepository bindings,
        IEnumerable<ISecretSource> sources,
        SecretResolverCacheState cacheState,
        SecretResolverMetrics metrics,
        ILogger<SecretResolver> logger,
        SecretAuthorityAcquisition authority)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(cacheState);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(authority);

        _bindings = bindings;
        _cacheState = cacheState;
        _metrics = metrics;
        _logger = logger;
        _provider = authority.Provider;
        _consumer = authority.Selection.Consumer;
        _infisicalEnvironment = authority.InfisicalOptions?.VaultEnvironment ?? string.Empty;
        _authorityEndpoint = authority.InfisicalOptions?.Origin.AbsoluteUri;
        _authorityProject = authority.InfisicalOptions?.ProjectId;

        // Index sources by SourceType. Duplicate types = bug => throw at startup.
        _sources = sources.ToFrozenDictionary(s => s.SourceType);
    }

    public async Task<RetainedSecretReference> CaptureAsync(
        string settingKey, Guid tenantId, CancellationToken cancellationToken)
    {
        var binding = await ResolveBindingAsync(settingKey, tenantId, cancellationToken).ConfigureAwait(false);
        if (binding is null || SelectedSourceType() != binding.SourceType
            || !SecretBindingAdmission.AllowsBinding(binding, SecretAcquisitionPolicy.Roots(_consumer), _infisicalEnvironment))
            throw new InvalidOperationException("storage_secret_unavailable");
        return RetainedSecretReference.Capture(binding, _provider.ToString(), _authorityEndpoint, _authorityProject);
    }

    public async Task<SecretResolutionResult> ResolveAsync(
        RetainedSecretReference reference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!string.Equals(reference.Authority, _provider.ToString(), StringComparison.Ordinal)
            || !string.Equals(reference.AuthorityEndpoint, _authorityEndpoint, StringComparison.Ordinal)
            || !string.Equals(reference.AuthorityProject, _authorityProject, StringComparison.Ordinal)
            || reference.SourceType != SelectedSourceType())
            return SecretResolutionResult.Invalid;

        // Retained references deliberately bypass the mutable binding cache and hierarchy.
        // A current binding with the same ID may now name a different external secret.
        return await ResolveBoundBindingAsync(reference.ToBinding(), cancellationToken,
            generation: 0, prefixes: [], useCache: false).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SecretResolutionResult> ResolveAsync(
        string settingKey,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingKey);

        long generation = _cacheState.CaptureGeneration();
        string instancePrefix = BuildCacheKeyPrefix(settingKey, SecretScope.Instance, null);
        string[] prefixes = tenantId.HasValue && tenantId.Value != Guid.Empty
            ? [BuildCacheKeyPrefix(settingKey, SecretScope.Tenant, tenantId), instancePrefix]
            : [instancePrefix];
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

            var result = await ResolveBoundBindingAsync(binding, cancellationToken, generation, prefixes).ConfigureAwait(false);
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

        long generation = _cacheState.CaptureGeneration();
        SecretBinding? binding = await _bindings.GetByTenantAndIdAsync(tenantId, bindingId, cancellationToken).ConfigureAwait(false);
        if (binding is null)
        {
            _metrics.RecordMiss(source: null);
            return SecretResolutionResult.Unconfigured;
        }

        return await ResolveBoundBindingAsync(binding, cancellationToken, generation,
            [BuildCacheKeyPrefix(binding.SettingKey, binding.Scope, binding.ScopeId)]).ConfigureAwait(false);
    }

    public async Task<SecretResolutionResult> ResolveQualifiedAsync(
        string settingKey,
        SecretScope scope,
        Guid? scopeId,
        string qualifier,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingKey);
        ArgumentNullException.ThrowIfNull(qualifier);
        if (qualifier.Length != 0 && string.IsNullOrWhiteSpace(qualifier))
            throw new ArgumentException("A qualifier must be empty or contain a binding identity.", nameof(qualifier));

        long generation = _cacheState.CaptureGeneration();
        string[] prefixes = [BuildCacheKeyPrefix(settingKey, scope, scopeId)];
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

            var result = await ResolveBoundBindingAsync(binding, cancellationToken, generation, prefixes).ConfigureAwait(false);
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
        _cacheState.Invalidate(BuildCacheKeyPrefix(settingKey, scope, scopeId));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Walks Tenant -> Instance -> authoritative registry default. A null return means
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
            SecretAuthorityKind.Environment or SecretAuthorityKind.UserSecrets => SecretBinding.CreateEnvironmentVariable(
                settingKey,
                SecretScope.Instance,
                scopeId: null,
                definition.DefaultEnvironmentVariableName),
            SecretAuthorityKind.Infisical when !string.IsNullOrWhiteSpace(_infisicalEnvironment) =>
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
        CancellationToken cancellationToken,
        long generation,
        string[] prefixes,
        bool useCache = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        binding = RuntimeSecretBinding.Capture(binding).ToBinding();
        var selectedSource = SelectedSourceType();
        if (selectedSource != binding.SourceType)
        {
            _logger.LogError("secret_source_invalid");
            _metrics.RecordError(binding.SourceType, SecretResolutionStatus.Invalid);
            return SecretResolutionResult.Invalid;
        }

        if (!SecretBindingAdmission.AllowsBinding(binding, SecretAcquisitionPolicy.Roots(_consumer), _infisicalEnvironment))
        {
            _metrics.RecordError(binding.SourceType, SecretResolutionStatus.Invalid);
            return SecretResolutionResult.Invalid;
        }

        if (binding.SettingKey is SecretDefinitionRegistry.Keys.Authentication.GoogleClientSecret
            or SecretDefinitionRegistry.Keys.Keycloak.BlazorClientSecret
            or SecretDefinitionRegistry.Keys.Keycloak.LifecycleClientSecret
            or SecretDefinitionRegistry.Keys.Atproto.OAuthClientPrivateJwks)
        {
            SecretDefinition definition = SecretDefinitionRegistry.GetRequired(binding.SettingKey);
            bool canonical = binding.Scope == SecretScope.Instance && binding.ScopeId is null
                && string.IsNullOrEmpty(binding.Qualifier)
                && (binding.SourceType == SecretSourceType.EnvironmentVariable
                    ? string.Equals(binding.EnvironmentVariableName,
                        definition.DefaultEnvironmentVariableName, StringComparison.Ordinal)
                    : !string.IsNullOrWhiteSpace(_infisicalEnvironment)
                        && string.Equals(binding.InfisicalEnvironment, _infisicalEnvironment, StringComparison.Ordinal)
                        && string.Equals(binding.InfisicalPath, definition.DefaultInfisicalPath, StringComparison.Ordinal)
                        && string.Equals(binding.InfisicalKey, definition.DefaultInfisicalKey, StringComparison.Ordinal));
            if (!canonical)
            {
                _logger.LogError("authentication_runtime_reference_invalid");
                _metrics.RecordError(binding.SourceType, SecretResolutionStatus.Invalid);
                return SecretResolutionResult.Invalid;
            }
        }

        if (!_sources.TryGetValue(binding.SourceType, out var source)
            || source is not ISecretRevisionOwner revisionOwner)
        {
            _logger.LogError("secret_source_invalid");
            _metrics.RecordError(binding.SourceType, SecretResolutionStatus.Invalid);
            return SecretResolutionResult.Invalid;
        }

        var logicalCacheKey = BuildCacheKey(binding.SettingKey, binding.Scope, binding.ScopeId, binding.Qualifier);
        var cacheKey = $"{logicalCacheKey}::{binding.SourceType}::{binding.Id:N}";
        if (useCache && _cacheState.Read(generation, prefixes, cacheKey) is { } cached)
        {
            if (!cached.IsResolved)
                return cached;
            if (revisionOwner.IsRevisionCurrent(binding, cached.Secret!.BindingRevision))
            {
                _metrics.RecordCacheHit();
                return cached;
            }
        }

        _metrics.RecordCacheMiss();
        SecretResolutionResult result;
        try
        {
            result = await source.GetSecretAsync(binding, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            _logger.LogError("secret_source_unavailable");
            _metrics.RecordError(binding.SourceType, SecretResolutionStatus.Unavailable);
            return SecretResolutionResult.Unavailable;
        }

        if (result.IsResolved && !revisionOwner.IsRevisionCurrent(binding, result.Secret!.BindingRevision))
            result = SecretResolutionResult.Unavailable;
        cancellationToken.ThrowIfCancellationRequested();
        if (useCache)
            result = _cacheState.Complete(generation, prefixes, logicalCacheKey, cacheKey, result);

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

        if (!revisionOwner.IsRevisionCurrent(binding, result.Secret!.BindingRevision))
            return SecretResolutionResult.Unavailable;
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
        SecretAuthorityKind.Environment or SecretAuthorityKind.UserSecrets => SecretSourceType.EnvironmentVariable,
        SecretAuthorityKind.Infisical => SecretSourceType.Infisical,
        _ => null,
    };
}
