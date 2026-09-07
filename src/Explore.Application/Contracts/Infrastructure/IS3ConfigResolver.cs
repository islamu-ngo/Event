using Explore.Application.Models;

namespace Explore.Application.Contracts.Infrastructure;

/// <summary>
/// Resolves non-secret S3 policy from governance and credentials from ISecretResolver.
/// </summary>
public interface IS3ConfigResolver
{
    /// <summary>
    /// Resolves the effective S3 configuration for the current tenant.
    /// Returns null if S3 is not configured (endpoint is empty/missing).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolved S3 configuration, or null if not configured.</returns>
    Task<S3Configuration?> ResolveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true when the effective S3 configuration has all required fields.
    /// </summary>
    Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates the cached S3 configuration.
    /// Call after S3 settings are changed in the admin UI.
    /// </summary>
    /// <param name="tenantId">Tenant to invalidate, or null for all tenants.</param>
    void InvalidateCache(Guid? tenantId = null);
}
