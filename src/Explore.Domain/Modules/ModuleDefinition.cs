namespace Explore.Domain.Modules;

/// <summary>
/// Defines an available module (aspect category) in the system.
/// Modules group related functionality that can be enabled/disabled per tenant.
/// </summary>
public class ModuleDefinition
{
    /// <summary>
    /// Primary key - UUID v7 for time-ordered IDs.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Unique module key (e.g., "Mod_Core", "Mod_Islamic", "Mod_Tech").
    /// Used for programmatic lookups and configuration.
    /// </summary>
    public required string ModuleKey { get; set; }

    /// <summary>
    /// Display name for the module shown in UI.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Description of what this module provides.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// URL to JSON schema defining wizard form fields for this module.
    /// Used by frontend to dynamically render aspect-specific forms.
    /// </summary>
    public string? WizardSchemaUrl { get; set; }

    /// <summary>
    /// Icon name for UI display (Material Design icon name).
    /// </summary>
    public string? IconName { get; set; }

    /// <summary>
    /// Display order in module selection UI.
    /// Lower numbers appear first.
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Whether this module is globally enabled.
    /// When false, no tenant can use this module.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Category grouping for admin UI (e.g., "Domain", "Integration", "Analytics").
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// When this module was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When this module was last updated.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
}
