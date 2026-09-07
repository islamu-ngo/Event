// for user-specific setting overrides.

namespace Explore.Application.Contracts.Persistence;

using Explore.Domain;

/// <summary>
/// Repository for user-specific preference overrides.
/// </summary>
public interface IUserPreferenceRepository : IGenericRepository<UserPreference, Guid>
{
    Task<UserPreference?> GetByUserAndKey(Guid tenantId, Guid userId, string key);
    Task<List<UserPreference>> GetAllForUser(Guid tenantId, Guid userId);
    Task<bool> RemoveOverride(Guid tenantId, Guid userId, string key);
}
