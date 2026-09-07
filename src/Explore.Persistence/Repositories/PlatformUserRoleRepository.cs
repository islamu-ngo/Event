using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class PlatformUserRoleRepository : GenericRepository<PlatformUserRole, Guid>, IPlatformUserRoleRepository
{
    private readonly ExploreDbContext _dbContext;

    public PlatformUserRoleRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> IsUserPlatformAdmin(Guid userId)
    {
        return await _dbContext.PlatformUserRoles
            .AsNoTracking()
            .Include(x => x.Role)
            .AnyAsync(x => x.UserId == userId
                && x.Role.RoleScopeId == (int)RoleScopeEnum.Platform
                && x.Role.MasterCode == "platform.admin");
    }

    public async Task<PlatformUserRole?> GetByUserAndRole(Guid userId, int roleId)
    {
        return await _dbContext.PlatformUserRoles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.RoleId == roleId);
    }

    public async Task<bool> HasAnyPlatformAdmin()
    {
        return await _dbContext.PlatformUserRoles
            .AsNoTracking()
            .Include(x => x.Role)
            .AnyAsync(x => x.Role.RoleScopeId == (int)RoleScopeEnum.Platform && x.Role.MasterCode == "platform.admin");
    }
}
