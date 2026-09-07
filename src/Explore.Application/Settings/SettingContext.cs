namespace Explore.Application.Settings;

/// <summary>
/// Identifies the scope chain for hierarchical setting resolution.
/// The resolver walks from Instance → Tenant → Organization → Group → User,
/// stopping at the deepest scope provided.
/// </summary>
/// <param name="TenantId">Tenant to resolve for. Null = instance-only.</param>
/// <param name="OrganizationId">Organization within the tenant. Null = skip org scope.</param>
/// <param name="GroupId">Group within the organization. Null = skip group scope.</param>
/// <param name="UserId">User preferences. Null = skip user scope.</param>
public sealed record SettingContext(
    Guid? TenantId = null,
    Guid? OrganizationId = null,
    Guid? GroupId = null,
    Guid? UserId = null);
