using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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

    public Task AssignPermissionsAsync(int roleId, IEnumerable<int> permissionIds, Guid? grantedBy = null) =>
        MutatePermissionsAsync(roleId, permissionIds, replace: false, grantedBy);

    public Task ReplacePermissionsAsync(int roleId, IEnumerable<int> permissionIds, Guid? grantedBy = null) =>
        MutatePermissionsAsync(roleId, permissionIds, replace: true, grantedBy);

    public Task RemoveAllPermissionsAsync(int roleId) =>
        MutatePermissionsAsync(roleId, [], replace: true, grantedBy: null);

    private async Task MutatePermissionsAsync(int roleId, IEnumerable<int> permissionIds, bool replace, Guid? grantedBy)
    {
        ArgumentNullException.ThrowIfNull(permissionIds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(roleId);
        var requested = permissionIds.Distinct().Order().ToArray();
        if (requested.Any(id => id <= 0))
            throw new ArgumentException("Permission identifiers must be positive.", nameof(permissionIds));

        if (_dbContext.Database.CurrentTransaction is { } transaction)
        {
            if (transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
                throw new InvalidOperationException("Role permission mutation requires a serializable transaction.");
            await ApplyAsync(CancellationToken.None);
        }
        else
        {
            if (_dbContext.ChangeTracker.HasChanges())
                throw new InvalidOperationException("Role permission replay cannot own pending caller changes.");
            await new EfCoreUnitOfWork(_dbContext).ExecuteSerializableAsync(async token =>
            {
                await ApplyAsync(token);
                return true;
            });
        }

        async Task ApplyAsync(CancellationToken token)
        {
            // The Role anchor also fences first assignment when no join row exists.
            await RelationalEntityRowFence.AcquireLookupAsync<Role>(_dbContext, [roleId], token);
            if (!await _dbContext.Roles.AsNoTracking().AnyAsync(row => row.Id == roleId, token))
                throw new ArgumentException("Role does not exist.", nameof(roleId));
            var existing = await _dbContext.RolePermissions.Where(row => row.RoleId == roleId).ToListAsync(token);
            var permissionSources = existing.Select(row => row.PermissionId).Concat(requested).Distinct().Order().ToArray();
            foreach (var permissionId in permissionSources)
                await RelationalEntityRowFence.AcquireLookupAsync<Permission>(_dbContext, [permissionId], token);
            if (await _dbContext.Permissions.AsNoTracking().CountAsync(row => requested.Contains(row.Id), token) != requested.Length)
                throw new ArgumentException("Permission does not exist.", nameof(permissionIds));

            var retained = existing.Select(row => row.PermissionId).ToHashSet();
            if (replace)
                _dbContext.RolePermissions.RemoveRange(existing.Where(row => !requested.Contains(row.PermissionId)));
            var now = DateTime.UtcNow;
            foreach (var permissionId in requested.Where(id => !retained.Contains(id)))
                _dbContext.RolePermissions.Add(new RolePermission
                {
                    RoleId = roleId,
                    Role = null!,
                    PermissionId = permissionId,
                    Permission = null!,
                    GrantedAt = now,
                    GrantedBy = grantedBy
                });
            await _dbContext.SaveChangesAsync(token);
        }
    }

    public async Task<Role?> DeleteUnusedCustomRoleAsync(int roleId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(roleId);
        if (_dbContext.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Role deletion cannot own pending caller changes.");
        if (_dbContext.Database.CurrentTransaction is { } transaction)
        {
            if (transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
                throw new InvalidOperationException("Role deletion requires a serializable transaction.");
            return await DeleteAsync(cancellationToken);
        }
        return await new EfCoreUnitOfWork(_dbContext).ExecuteSerializableAsync(DeleteAsync, cancellationToken);

        async Task<Role?> DeleteAsync(CancellationToken token)
        {
            await RelationalEntityRowFence.AcquireLookupAsync<Role>(_dbContext, [roleId], token);
            var role = await _dbContext.Roles.AsNoTracking().SingleOrDefaultAsync(row => row.Id == roleId, token);
            if (role is null) return null;
            if (role.IsSystem)
                throw new BadRequestException("A system role cannot be deleted.");

            var permissionIds = await _dbContext.RolePermissions.AsNoTracking().Where(row => row.RoleId == roleId)
                .OrderBy(row => row.PermissionId).Select(row => row.PermissionId).ToArrayAsync(token);
            foreach (var permissionId in permissionIds)
                await RelationalEntityRowFence.AcquireLookupAsync<Permission>(_dbContext, [permissionId], token);

            // A global Role cannot be deleted while another tenant or retained history references it.
            if (await _dbContext.PlatformUserRoles.AsNoTracking().AnyAsync(row => row.RoleId == roleId, token)
                || await _dbContext.OrganizationMembers.IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
                    .AnyAsync(row => row.RoleId == roleId, token)
                || await _dbContext.GroupMembers.IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
                    .AnyAsync(row => row.RoleId == roleId, token)
                || await _dbContext.TenantUserRoleGrants.IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
                    .AnyAsync(row => row.RoleId == roleId, token)
                || await _dbContext.EventRoleAssignments.IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
                    .AnyAsync(row => row.RoleId == roleId, token)
                || await _dbContext.TenantInvitations.IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
                    .AnyAsync(row => row.RoleId == roleId, token))
                throw new BadRequestException("Cannot delete a role that still has assignments or retained references.");

            // The existing FK cascades only the permission joins; no earlier grant-removal commit exists.
            if (await _dbContext.Roles.Where(row => row.Id == roleId && !row.IsSystem).ExecuteDeleteAsync(token) != 1)
                throw new DbUpdateConcurrencyException("The role changed before deletion.");
            foreach (var entry in _dbContext.ChangeTracker.Entries<Role>().Where(entry => entry.Entity.Id == roleId).ToArray())
                entry.State = EntityState.Detached;
            foreach (var entry in _dbContext.ChangeTracker.Entries<RolePermission>().Where(entry => entry.Entity.RoleId == roleId).ToArray())
                entry.State = EntityState.Detached;
            return role;
        }
    }

    public async Task<bool> HasActiveMembersAsync(int roleId)
    {
        var hasOrgMembers = await _dbContext.OrganizationMembers
            .AnyAsync(om => om.RoleId == roleId && !om.IsDeleted);

        if (hasOrgMembers) return true;

        if (await _dbContext.GroupMembers.AnyAsync(member => member.RoleId == roleId && !member.IsDeleted))
            return true;

        var hasTenantRoleGrants = await _dbContext.TenantUserRoleGrants
            .AnyAsync(grant => grant.RoleId == roleId && grant.RevokedAt == null);

        if (hasTenantRoleGrants) return true;

        return await _dbContext.EventRoleAssignments
            .AnyAsync(a => a.RoleId == roleId);
    }
}
