// Allows users to customize settings unless locked at a higher scope.

namespace Explore.Domain;

using Explore.Domain.Interfaces;

/// <summary>
/// User-specific preference/override for a setting.
/// Only created when a user explicitly overrides the parent scope default.
/// UserPreference is tenant-scoped so user preferences can differ per tenant.
/// </summary>
public class UserPreference : ITenantEntity, IAuditableEntity
{
    public Guid Id { get; set; }

    /// <summary>
    /// The tenant this preference belongs to.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Navigation property to the tenant.
    /// </summary>
    public required Tenant Tenant { get; set; }

    /// <summary>
    /// The external user ID (from Keycloak).
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// The setting key being overridden.
    /// </summary>
    public required string SettingKey { get; set; }

    /// <summary>
    /// JSON-serialized value of the user's preference.
    /// </summary>
    public required string Value { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
