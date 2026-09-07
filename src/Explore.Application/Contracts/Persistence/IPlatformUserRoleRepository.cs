using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IPlatformUserRoleRepository : IGenericRepository<PlatformUserRole, Guid>
{
    Task<bool> IsUserPlatformAdmin(Guid userId);
    Task<PlatformUserRole?> GetByUserAndRole(Guid userId, int roleId);
    Task<bool> HasAnyPlatformAdmin();
}
