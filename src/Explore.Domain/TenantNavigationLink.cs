using Explore.Domain.Interfaces;

namespace Explore.Domain;

/// <summary>
/// Represents a customizable navigation link for a tenant.
/// Allows tenants to define custom navigation items in their navbar.
/// </summary>
public class TenantNavigationLink : ITenantEntity, IAuditableEntity, ISoftDeletable
{
    /// <summary>
    /// Unique identifier for this navigation link.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Tenant identifier - marks this entity as tenant-scoped.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Display label for the navigation link.
    /// Maximum 50 characters.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// URL or route that the navigation link points to.
    /// Maximum 500 characters.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Optional icon identifier or CSS class for the navigation link.
    /// Can be null if no icon is desired.
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Display order of the navigation link.
    /// Lower values appear first in the navbar.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Indicates whether the link should open in a new tab/window.
    /// </summary>
    public bool OpenInNewTab { get; set; }

    /// <summary>
    /// Indicates whether this navigation link is currently active/visible.
    /// </summary>
    public bool IsActive { get; set; } = true;

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    // ISoftDeletable
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    /// <summary>
    /// Navigation property to the parent Tenant.
    /// </summary>
    public Tenant? Tenant { get; set; }
}
