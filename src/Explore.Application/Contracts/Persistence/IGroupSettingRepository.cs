// for group-specific setting overrides.

namespace Explore.Application.Contracts.Persistence;

using Explore.Domain;

/// <summary>
/// Repository for group-specific setting overrides.
/// </summary>
public interface IGroupSettingRepository : IGenericRepository<GroupSetting, Guid>
{
    Task<GroupSetting?> GetByGroupAndKey(Guid groupId, string key);
    Task<List<GroupSetting>> GetAllForGroup(Guid groupId);
    Task<bool> RemoveOverride(Guid groupId, string key);
}
