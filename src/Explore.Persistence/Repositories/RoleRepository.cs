using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class RoleRepository : GenericRepository<Role, int>, IRoleRepository
{
    private readonly ExploreDbContext _dbContext;

    public RoleRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<Role>> GetByScopeAsync(RoleScopeEnum scope)
    {
        return await GetByScopeIdAsync((int)scope);
    }

    public async Task<IReadOnlyList<Role>> GetByScopeIdAsync(int roleScopeId)
    {
        return await _dbContext.Roles
            .AsNoTracking()
            .Where(r => r.RoleScopeId == roleScopeId)
            .OrderBy(r => r.Id)
            .ToListAsync();
    }

    public async Task<Role?> GetByMasterCodeAsync(string masterCode)
    {
        return await _dbContext.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.MasterCode == masterCode);
    }

    public async Task<Role?> GetByIdAsync(int id)
    {
        return await GetById(id);
    }

    public async Task<IReadOnlyList<Role>> GetAllAsync()
    {
        return await GetAll();
    }

    public async Task<IReadOnlyList<Permission>> GetPermissionsForRoleAsync(int roleId)
    {
        return await _dbContext.RolePermissions
            .AsNoTracking()
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.Permission)
            .ToListAsync();
    }

    public async Task AssignPermissionsAsync(int roleId, IEnumerable<int> permissionIds, Guid? grantedBy = null)
    {
        var now = DateTime.UtcNow;

        foreach (var permissionId in permissionIds)
        {
            var exists = await _dbContext.RolePermissions
                .AnyAsync(rp => rp.RoleId == roleId && rp.PermissionId == permissionId);

            if (!exists)
            {
                _dbContext.RolePermissions.Add(new RolePermission
                {
                    RoleId = roleId,
                    PermissionId = permissionId,
                    Role = null!,
                    Permission = null!,
                    GrantedAt = now,
                    GrantedBy = grantedBy
                });
            }
        }

        await _dbContext.SaveChangesAsync();
    }

    public async Task ReplacePermissionsAsync(int roleId, IEnumerable<int> permissionIds, Guid? grantedBy = null)
    {
        var existing = await _dbContext.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .ToListAsync();

        _dbContext.RolePermissions.RemoveRange(existing);

        var now = DateTime.UtcNow;
        foreach (var permissionId in permissionIds)
        {
            _dbContext.RolePermissions.Add(new RolePermission
            {
                RoleId = roleId,
                PermissionId = permissionId,
                Role = null!,
                Permission = null!,
                GrantedAt = now,
                GrantedBy = grantedBy
            });
        }

        await _dbContext.SaveChangesAsync();
    }

    public async Task RemoveAllPermissionsAsync(int roleId)
    {
        var existing = await _dbContext.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .ToListAsync();

        _dbContext.RolePermissions.RemoveRange(existing);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<bool> HasActiveMembersAsync(int roleId)
    {
        var hasOrgMembers = await _dbContext.OrganizationMembers
            .AnyAsync(om => om.RoleId == roleId && !om.IsDeleted);

        if (hasOrgMembers) return true;

        var hasTenantRoleGrants = await _dbContext.TenantUserRoleGrants
            .AnyAsync(grant => grant.RoleId == roleId && grant.RevokedAt == null);

        if (hasTenantRoleGrants) return true;

        return await _dbContext.EventRoleAssignments
            .AnyAsync(a => a.RoleId == roleId);
    }
}
