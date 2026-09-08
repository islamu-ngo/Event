using Explore.Domain.Interfaces;

namespace Explore.Domain;

/// <summary>
/// A named column/section of footer links belonging to a tenant.
/// When <see cref="TenantId"/> is null the group is an instance-level default shown
/// to tenants that have no own groups configured, or when the instance locks tenant link-group editing.
/// </summary>
public class TenantFooterLinkGroup : IAuditableEntity
{
    public Guid Id { get; set; }

    /// <summary>
    /// Owning tenant. Null = instance-level default group.
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>Column heading shown above the links (e.g., "Platform", "Legal").</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Display order among groups for the same tenant. Lower values appear first.</summary>
    public int Order { get; set; }

    /// <summary>Whether this group is currently visible in the footer.</summary>
    public bool IsActive { get; set; } = true;

    // ── IAuditableEntity ────────────────────────────────────────────────────
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    // ── Navigation properties (readonly — writes via repository) ─────────────
    public Tenant? Tenant { get; private set; }

    private readonly List<TenantFooterLink> _links = [];
    public IReadOnlyList<TenantFooterLink> Links => _links.AsReadOnly();
}
