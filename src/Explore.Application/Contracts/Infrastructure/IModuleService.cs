// (aspect categories) are available to each tenant.

namespace Explore.Application.Contracts.Infrastructure;

/// <summary>
/// Service for module governance and discovery.
/// Controls which modules (aspect categories) are available to tenants.
/// </summary>
public interface IModuleService
{
    /// <summary>
    /// Gets all modules that are globally available (active in the system).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of available modules.</returns>
    Task<IReadOnlyList<ModuleInfo>> GetAllModulesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all modules enabled for a specific tenant.
    /// </summary>
    /// <param name="tenantId">The tenant to get modules for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of enabled modules for the tenant.</returns>
    Task<IReadOnlyList<ModuleInfo>> GetEnabledModulesAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a specific module is enabled for a tenant.
    /// </summary>
    /// <param name="tenantId">The tenant to check.</param>
    /// <param name="moduleKey">The module key (e.g., "Mod_Islamic").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the module is enabled for the tenant.</returns>
    Task<bool> IsModuleEnabledAsync(Guid tenantId, string moduleKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the wizard schema URL for a module (used for dynamic form generation).
    /// </summary>
    /// <param name="moduleKey">The module key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The schema URL, or null if not available.</returns>
    Task<string?> GetModuleWizardSchemaUrlAsync(string moduleKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables a module for a tenant.
    /// </summary>
    /// <param name="tenantId">The tenant to enable the module for.</param>
    /// <param name="moduleKey">The module key to enable.</param>
    /// <param name="enabledBy">The user enabling the module.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful, false if module doesn't exist or isn't active.</returns>
    Task<bool> EnableModuleAsync(Guid tenantId, string moduleKey, Guid? enabledBy = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disables a module for a tenant.
    /// </summary>
    /// <param name="tenantId">The tenant to disable the module for.</param>
    /// <param name="moduleKey">The module key to disable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful, false if capability doesn't exist.</returns>
    Task<bool> DisableModuleAsync(Guid tenantId, string moduleKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates the module cache for a tenant.
    /// </summary>
    /// <param name="tenantId">The tenant to invalidate, or null for all tenants.</param>
    void InvalidateCache(Guid? tenantId = null);
}

/// <summary>
/// Information about a module for API responses.
/// </summary>
public class ModuleInfo
{
    /// <summary>
    /// The module's unique identifier.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Unique module key (e.g., "Mod_Islamic", "Mod_Tech").
    /// </summary>
    public string ModuleKey { get; init; } = string.Empty;

    /// <summary>
    /// Display name for the module.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Description of what this module provides.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Material Design icon name for UI display.
    /// </summary>
    public string? IconName { get; init; }

    /// <summary>
    /// Category grouping (e.g., "Core", "Domain", "Integration").
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Display order for sorting.
    /// </summary>
    public int DisplayOrder { get; init; }

    /// <summary>
    /// URL to the wizard schema for dynamic form generation.
    /// </summary>
    public string? WizardSchemaUrl { get; init; }

    /// <summary>
    /// Whether this module is globally active.
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// Whether this module is enabled for the current tenant (populated only in tenant-specific queries).
    /// </summary>
    public bool? IsEnabledForTenant { get; init; }
}
