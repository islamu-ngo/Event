using Explore.Application.Models;

namespace Explore.Application.Contracts.Infrastructure;

/// <summary>
/// Resolves analytics configuration from the cascading settings engine.
/// <para>
/// Resolution order:
/// 1. Check if settings are locked at system level (instance admin enforces SaaS-wide analytics)
/// 2. Check for tenant-specific override (tenant chooses their own provider)
/// 3. Fall back to system default
/// </para>
/// <para>
/// This enables flexible SaaS scenarios:
/// - Instance admin locks analytics provider -> all tenants use the same provider
/// - Instance admin unlocks analytics -> tenants can choose Posthog, Plausible, Rybbit, RudderStack, or None
/// - Default analytics is set at instance level -> tenants use it unless they override
/// </para>
/// </summary>
public interface IAnalyticsConfigResolver
{
    /// <summary>
    /// Resolves the effective analytics configuration for the current tenant.
    /// Always returns a non-null configuration (defaults to Provider=None, IsEnabled=false).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolved analytics configuration.</returns>
    Task<AnalyticsConfiguration> ResolveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates the cached analytics configuration.
    /// Call after analytics settings are changed in the admin UI.
    /// </summary>
    /// <param name="tenantId">Tenant to invalidate, or null for all tenants.</param>
    void InvalidateCache(Guid? tenantId = null);
}
