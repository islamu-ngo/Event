namespace Explore.Application.Authorization;

/// <summary>
/// Authorization scope context used by Cerbos for per-tenant and per-organization policy resolution.
/// <para>
/// Cerbos evaluates policies using scope to select tenant- or org-specific overrides.
/// This record captures the scope hierarchy for a resource being authorized.
/// </para>
/// </summary>
/// <param name="TenantId">Tenant owning the resource. Used for Cerbos scope-based policy resolution.</param>
/// <param name="OrganizationId">Organization owning the resource, when applicable.</param>
public sealed record AuthorizationScope(
    string? TenantId = null,
    string? OrganizationId = null)
{
    /// <summary>Empty scope for resources not bound to a tenant or organization.</summary>
    public static readonly AuthorizationScope Empty = new();
}
