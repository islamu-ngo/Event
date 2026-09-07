// data access for module governance and discovery.

using Explore.Domain.Modules;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Repository for module definitions.
/// </summary>
public interface IModuleDefinitionRepository : IGenericRepository<ModuleDefinition, Guid>
{
    /// <summary>
    /// Gets a module by its unique key (e.g., "Mod_Islamic").
    /// </summary>
    Task<ModuleDefinition?> GetByKey(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active modules ordered by display order.
    /// </summary>
    Task<List<ModuleDefinition>> GetAllActive();

    Task<IReadOnlyList<ModuleDefinition>> GetActiveByKeysAsync(
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a module exists and is active.
    /// </summary>
    Task<bool> IsActive(string key);
}
