using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class PermissionRepository : GenericRepository<Permission, int>, IPermissionRepository
{
    private readonly ExploreDbContext _dbContext;

    public PermissionRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Permission?> GetByMasterCodeAsync(string masterCode)
    {
        return await _dbContext.Permissions
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.MasterCode == masterCode);
    }

    public async Task<IReadOnlyList<Permission>> GetByResourceKindAsync(string resourceKind)
    {
        return await _dbContext.Permissions
            .AsNoTracking()
            .Where(p => p.ResourceKind == resourceKind && p.IsActive)
            .OrderBy(p => p.Action)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Permission>> GetByScopeAsync(RoleScopeEnum scope)
    {
        return await _dbContext.Permissions
            .AsNoTracking()
            .Where(p => p.RoleScopeId == (int)scope && p.IsActive)
            .OrderBy(p => p.GroupName)
            .ThenBy(p => p.ResourceKind)
            .ThenBy(p => p.Action)
            .ToListAsync();
    }

    public async Task<bool> HasPermissionAsync(IEnumerable<int> roleIds, string permissionMasterCode)
    {
        var roleIdList = roleIds.ToList();
        if (roleIdList.Count == 0)
            return false;

        return await _dbContext.RolePermissions
            .AsNoTracking()
            .AnyAsync(rp =>
                roleIdList.Contains(rp.RoleId) &&
                rp.Permission.MasterCode == permissionMasterCode &&
                rp.Permission.IsActive);
    }

    public async Task<IReadOnlyList<Permission>> GetAssignablePermissionsAsync(
        IEnumerable<int> callerRoleIds,
        RoleScopeEnum targetScope)
    {
        var callerRoleIdList = callerRoleIds.ToList();

        // Get permissions the caller has (capability ceiling: can only grant what you have)
        var callerPermissionIds = await _dbContext.RolePermissions
            .AsNoTracking()
            .Where(rp => callerRoleIdList.Contains(rp.RoleId))
            .Select(rp => rp.PermissionId)
            .Distinct()
            .ToListAsync();

        // Filter to: active permissions the caller has, matching target scope, excluding filtered
        return await _dbContext.Permissions
            .AsNoTracking()
            .Where(p =>
                p.IsActive &&
                !p.IsFiltered &&
                p.RoleScopeId >= (int)targetScope &&
                callerPermissionIds.Contains(p.Id))
            .OrderBy(p => p.GroupName)
            .ThenBy(p => p.ResourceKind)
            .ThenBy(p => p.Action)
            .ToListAsync();
    }
}
