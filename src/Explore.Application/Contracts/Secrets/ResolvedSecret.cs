using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Secrets;

/// <summary>
/// Carries a resolved secret value together with the provenance metadata needed for auditing,
/// metrics, and downstream cache invalidation. The <see cref="Value"/> is sensitive — never log it,
/// never serialize it to a non-admin surface, and scrub it from any exception messages.
/// </summary>
/// <param name="SettingKey">Canonical setting key (see <see cref="Explore.Domain.Secrets.SecretDefinitionRegistry.Keys"/>).</param>
/// <param name="Value">The plaintext secret value materialized from the binding's single declared source.</param>
/// <param name="Source">The source type that produced <paramref name="Value"/> — identical to the binding's <see cref="Explore.Domain.Secrets.SecretBinding.SourceType"/>.</param>
/// <param name="Scope">Scope at which the binding was registered.</param>
/// <param name="ScopeId">Tenant id when <paramref name="Scope"/> is <see cref="SecretScope.Tenant"/>, otherwise <c>null</c>.</param>
/// <param name="ResolvedAt">UTC timestamp of resolution (useful for TTL accounting and audit correlation).</param>
public sealed record ResolvedSecret(
    string SettingKey,
    string Value,
    SecretSourceType Source,
    SecretScope Scope,
    Guid? ScopeId,
    DateTimeOffset ResolvedAt);
