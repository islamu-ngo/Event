// Allows tenants to customize settings unless locked at the system level.

namespace Explore.Domain;

using Explore.Domain.Interfaces;

/// <summary>
/// Tenant-specific override for a system setting.
/// Only created when a tenant explicitly overrides the system default.
/// </summary>
public class TenantSetting : ITenantEntity, IAuditableEntity
{
    /// <summary>
    /// Unique identifier for this tenant setting override.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The tenant this setting belongs to.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Navigation property to the tenant.
    /// </summary>
    public required Tenant Tenant { get; set; }

    /// <summary>
    /// The setting key being overridden (must match a SystemSetting.Key).
    /// </summary>
    public required string SettingKey { get; set; }

    /// <summary>
    /// JSON-serialized value of the tenant's override.
    /// </summary>
    public required string Value { get; set; }

    /// <summary>
    /// When true, lower-scope overrides (Organization, Group, User) for this key become non-effective.
    /// The tenant's value becomes authoritative until unlocked.
    /// </summary>
    public bool IsLocked { get; set; }

    /// <summary>
    /// When this override was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Who created this override.
    /// </summary>
    public Guid? CreatedBy { get; set; }

    /// <summary>
    /// When this override was last modified.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Who last modified this override.
    /// </summary>
    public Guid? UpdatedBy { get; set; }
}
