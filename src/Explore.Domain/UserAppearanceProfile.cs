namespace Explore.Domain;

using Explore.Domain.Enums;
using Explore.Domain.Interfaces;
using Explore.Domain.ValueObjects;

public class UserAppearanceProfile : IAuditableEntity
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>
    /// Null means this is a global account profile;
    /// non-null means this is a tenant-specific user profile.
    /// </summary>
    public Guid? TenantId { get; set; }

    public required string Name { get; set; }

    public AppearanceThemeMode ThemeMode { get; set; } = AppearanceThemeMode.System;

    /// <summary>Snapshot of light palette colors at the moment of creation or last edit.</summary>
    public required UiThemePalette LightPaletteSnapshot { get; set; }

    /// <summary>Snapshot of dark palette colors at the moment of creation or last edit.</summary>
    public required UiThemePalette DarkPaletteSnapshot { get; set; }

    /// <summary>The ThemeKey of the source preset, if this profile was cloned from one.</summary>
    public string? SourcePresetKey { get; set; }

    /// <summary>The Id of the source preset, if this profile was cloned from one.</summary>
    public Guid? SourcePresetId { get; set; }

    /// <summary>The SeedVersion of the source preset at clone time — enables "update to latest" in the future.</summary>
    public int? SourcePresetSeedVersion { get; set; }

    /// <summary>Whether the user can edit this profile's colors directly.</summary>
    public bool IsUserEditable { get; set; } = true;

    /// <summary>Whether this profile is the user's default for the given scope.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Soft-archival flag — hides from quick switcher without deletion.</summary>
    public bool IsArchived { get; set; }

    /// <summary>When the snapshot was cloned from the source preset. Null for fully custom profiles.</summary>
    public DateTimeOffset? ClonedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
