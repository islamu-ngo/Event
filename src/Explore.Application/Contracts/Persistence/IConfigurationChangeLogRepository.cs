// Provides query methods for retrieving audit trail entries by scope, user, or setting key.

using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Repository for configuration change audit log entries.
/// </summary>
public interface IConfigurationChangeLogRepository : IGenericRepository<ConfigurationChangeLog, Guid>
{
    /// <summary>
    /// Gets change log entries for a specific setting key.
    /// </summary>
    Task<List<ConfigurationChangeLog>> GetBySettingKey(string settingKey, int limit = 50);

    /// <summary>
    /// Gets change log entries for a specific scope (e.g., all tenant-level changes for a given tenant).
    /// </summary>
    Task<List<ConfigurationChangeLog>> GetByScope(ConfigurationScopeEnum scope, Guid? scopeId = null, int limit = 50);

    /// <summary>
    /// Gets change log entries made by a specific user.
    /// </summary>
    Task<List<ConfigurationChangeLog>> GetByUserId(Guid userId, int limit = 50);
}
