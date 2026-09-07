using Explore.Domain.Interfaces;

namespace Explore.Domain;

/// <summary>
/// A single link entry inside a <see cref="TenantFooterLinkGroup"/> footer column.
/// </summary>
public class TenantFooterLink : IAuditableEntity
{
    public Guid Id { get; set; }

    /// <summary>Parent group this link belongs to.</summary>
    public Guid FooterLinkGroupId { get; set; }

    /// <summary>Visible link text.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Destination URL or relative route.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>When true the link opens in a new browser tab.</summary>
    public bool OpenInNewTab { get; set; }

    /// <summary>Display order within the parent group. Lower values appear first.</summary>
    public int Order { get; set; }

    /// <summary>Whether this link is currently visible in the footer.</summary>
    public bool IsActive { get; set; } = true;

    // ── IAuditableEntity ────────────────────────────────────────────────────
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    // ── Navigation properties (readonly) ────────────────────────────────────
    public TenantFooterLinkGroup? Group { get; private set; }
}
