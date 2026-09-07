using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Secrets;

/// <summary>
/// Resolves a setting-key to its plaintext value by dispatching to exactly one <see cref="ISecretSource"/>
/// determined by the corresponding <see cref="Explore.Domain.Secrets.SecretBinding"/>. The resolver MUST NOT
/// implement a fallback chain — if the declared source cannot produce a value the typed result preserves why,
/// even if other sources could have yielded a value.
/// </summary>
public interface ISecretResolver
{
    /// <summary>
    /// Returns the resolved secret for <paramref name="settingKey"/> at the given scope. Attempts the
    /// tenant scope first (when <paramref name="tenantId"/> is provided) and falls back to the instance
    /// scope only if no tenant-scoped binding exists. The fallback is at the <em>binding-lookup</em>
    /// layer, never at the <em>source</em> layer.
    /// </summary>
    /// <param name="settingKey">Canonical setting key from <see cref="Explore.Domain.Secrets.SecretDefinitionRegistry.Keys"/>.</param>
    /// <param name="tenantId">The active tenant id, or <c>null</c> to resolve against the instance scope only.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A bounded outcome distinguishing unconfigured, unavailable, unauthorized, and invalid states.
    /// </returns>
    Task<SecretResolutionResult> ResolveAsync(
        string settingKey,
        Guid? tenantId,
        CancellationToken cancellationToken = default);

    Task<SecretResolutionResult> ResolveQualifiedAsync(
        string settingKey,
        SecretScope scope,
        Guid? scopeId,
        string qualifier,
        CancellationToken cancellationToken = default);

    Task<SecretResolutionResult> ResolveTenantBindingAsync(
        Guid tenantId,
        Guid bindingId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evicts the cached value (if any) for a given key/scope. Called by notification handlers when a
    /// <see cref="Explore.Domain.Secrets.SecretBinding"/> row changes.
    /// </summary>
    Task InvalidateAsync(
        string settingKey,
        SecretScope scope,
        Guid? scopeId,
        CancellationToken cancellationToken = default);
}
