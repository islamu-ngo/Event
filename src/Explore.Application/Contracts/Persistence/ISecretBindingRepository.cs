// for secret resolution. Stores where a secret value lives, never the value itself.

namespace Explore.Application.Contracts.Persistence;

using Explore.Domain.Enums;
using Explore.Domain.Secrets;

/// <summary>
/// Repository for <see cref="SecretBinding"/> records.
/// A binding declares, for a given <paramref name="settingKey"/> at a given scope,
/// where the runtime value is stored (Infisical / environment variable / inline-encrypted).
/// The repository never stores or surfaces the actual secret value - only the
/// binding metadata, source type, and validation state.
/// </summary>
public interface ISecretBindingRepository : IGenericRepository<SecretBinding, Guid>
{
    /// <summary>
    /// Gets the binding for a specific setting key at a specific scope, if any.
    /// </summary>
    /// <param name="settingKey">Canonical setting key from <c>SecretDefinitionRegistry.Keys</c>.</param>
    /// <param name="scope">The scope at which the binding is declared.</param>
    /// <param name="scopeId">Tenant id when <paramref name="scope"/> is <see cref="SecretScope.Tenant"/>; must be <c>null</c> when <see cref="SecretScope.Instance"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The binding if present; <c>null</c> when no binding exists at that scope (inherit).</returns>
    Task<SecretBinding?> GetByKeyAndScopeAsync(
        string settingKey,
        SecretScope scope,
        Guid? scopeId,
        CancellationToken cancellationToken = default);

    Task<SecretBinding?> GetByKeyScopeAndQualifierAsync(
        string settingKey,
        SecretScope scope,
        Guid? scopeId,
        string qualifier,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all bindings defined at a specific scope. Use with
    /// <c>scope=Instance, scopeId=null</c> to list instance-wide bindings,
    /// or <c>scope=Tenant, scopeId=tenantId</c> for a single tenant's overrides.
    /// </summary>
    Task<IReadOnlyList<SecretBinding>> GetByScopeAsync(
        SecretScope scope,
        Guid? scopeId,
        CancellationToken cancellationToken = default);

    Task<SecretBinding?> GetByTenantAndIdAsync(
        Guid tenantId,
        Guid bindingId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all bindings for a single setting key across all scopes - used by the
    /// resolver to walk the Tenant -> Instance hierarchy in one trip.
    /// </summary>
    Task<IReadOnlyList<SecretBinding>> GetAllForKeyAsync(
        string settingKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether an unqualified binding already exists at a given scope for a given key.
    /// </summary>
    Task<bool> ExistsForScopeAsync(
        string settingKey,
        SecretScope scope,
        Guid? scopeId,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsForScopeAndQualifierAsync(
        string settingKey,
        SecretScope scope,
        Guid? scopeId,
        string qualifier,
        CancellationToken cancellationToken = default);
}
