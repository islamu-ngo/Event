// Supports BYO (Bring Your Own) Cerbos per tenant and instance-managed isolation.

using Explore.Application.Models;

namespace Explore.Application.Contracts.Infrastructure;

/// <summary>
/// Resolves Cerbos PDP configuration from the cascading settings engine.
/// <para>
/// Resolution order:
/// 1. Check if tenant customization is enabled at instance level
/// 2. Check for tenant-specific BYO Cerbos override (custom endpoint)
/// 3. Fall back to the instance's Cerbos PDP endpoint
/// </para>
/// <para>
/// SaaS scenarios supported:
/// - Instance admin locks Cerbos settings → all tenants use the instance PDP
/// - Instance admin enables customization → tenants can bring their own Cerbos PDP
/// - Default: all tenants use instance PDP with scope-based policy isolation
/// </para>
/// </summary>
public interface ICerbosConfigResolver
{
    /// <summary>
    /// Resolves the effective Cerbos PDP configuration for the current tenant.
    /// Returns null if Cerbos is not configured at all (neither instance nor BYO).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolved Cerbos configuration, or null if not configured.</returns>
    Task<CerbosConfiguration?> ResolveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates the cached Cerbos configuration.
    /// Call after Cerbos settings are changed in the admin UI.
    /// </summary>
    /// <param name="tenantId">Tenant to invalidate, or null for all tenants.</param>
    void InvalidateCache(Guid? tenantId = null);
}
