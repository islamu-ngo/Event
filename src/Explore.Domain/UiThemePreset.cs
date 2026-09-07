namespace Explore.Domain;

using Explore.Domain.Interfaces;
using Explore.Domain.ValueObjects;

public class UiThemePreset : IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }

    /// <summary>
    /// Null means platform/system preset; non-null means tenant-created preset.
    /// </summary>
    public Guid? TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    /// <summary>
    /// Stable semantic key (e.g., "enterprise-blue", "emerald-green").
    /// The true identity contract — GUIDs are database identity only.
    /// </summary>
    public required string ThemeKey { get; set; }

    public required string DisplayName { get; set; }
    public string? Description { get; set; }

    public required UiThemePalette LightPalette { get; set; }
    public required UiThemePalette DarkPalette { get; set; }

    /// <summary>Whether this is an immutable system preset that cannot be edited or hard-deleted.</summary>
    public bool IsSystem { get; set; }

    /// <summary>Whether tenant admins can edit this preset. System presets are never editable.</summary>
    public bool IsEditable { get; set; } = true;

    /// <summary>Whether this preset appears in the catalog for new selections.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Incremented on each seed upgrade; used for idempotent seeding.</summary>
    public int SeedVersion { get; set; }

    /// <summary>Soft-delete timestamp — removes from catalog but preserves user profile lineage references.</summary>
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public bool IsDeleted { get; set; }

    /// <summary>Deprecation timestamp — signals that the preset is no longer recommended but remains functional.</summary>
    public DateTimeOffset? DeprecatedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
