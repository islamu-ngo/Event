using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class GroupMemberRepository : GenericRepository<GroupMember, Guid>, IGroupMemberRepository
{
    private readonly ExploreDbContext _dbContext;

    public GroupMemberRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<GroupMember>> GetGroupMembersWithDetails()
    {
        return await _dbContext.GroupMembers
            .AsNoTracking()
            .AsSplitQuery()
            .Include(m => m.User)
                .ThenInclude(u => u!.Pii)
            .Include(m => m.GroupTenant)
                .ThenInclude(p => p.Group)
            .Include(m => m.Role)
            .Include(m => m.GroupPosition)
            .ToListAsync();
    }

    public async Task<GroupMember?> GetGroupMemberWithDetails(Guid id)
    {
        return await _dbContext.GroupMembers
            .AsNoTracking()
            .AsSplitQuery()
            .Include(m => m.User)
                .ThenInclude(u => u!.Pii)
            .Include(m => m.GroupTenant)
                .ThenInclude(p => p.Group)
                    .ThenInclude(g => g.Actor)
            .Include(m => m.Role)
            .Include(m => m.GroupPosition)
            .FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task<List<GroupMember>> GetMembersByGroupId(Guid groupId)
    {
        return await _dbContext.GroupMembers
            .AsNoTracking()
            .AsSplitQuery()
            .Include(m => m.User)
                .ThenInclude(u => u!.Pii)
            .Include(m => m.Role)
            .Include(m => m.GroupPosition)
            .Include(m => m.GroupTenant)
                .ThenInclude(p => p.Group)
            .Where(m => m.GroupTenant.GroupId == groupId)
            .ToListAsync();
    }

    public async Task<GroupMember?> GetByGroupAndUser(Guid groupId, Guid userId)
    {
        return await _dbContext.GroupMembers
            .AsNoTracking()
            .Include(m => m.Role)
            .Include(m => m.GroupPosition)
            .Include(m => m.GroupTenant)
                .ThenInclude(p => p.Group)
            .FirstOrDefaultAsync(m => m.GroupTenant.GroupId == groupId && m.UserId == userId);
    }

    public async Task<bool> Exists(Guid groupId, Guid userId)
    {
        return await _dbContext.GroupMembers
            .AsNoTracking()
            .AnyAsync(m => m.GroupTenant.GroupId == groupId && m.UserId == userId);
    }

    public async Task<bool> HasPermissionInGroup(Guid groupId, Guid userId, string permissionMasterCode)
    {
        var roleId = await _dbContext.GroupMembers
            .AsNoTracking()
            .Where(m => m.GroupTenant.GroupId == groupId && m.UserId == userId)
            .Select(m => (int?)m.RoleId)
            .FirstOrDefaultAsync();

        if (roleId == null)
            return false;

        var hasPermission = await _dbContext.Set<RolePermission>()
            .AsNoTracking()
            .AnyAsync(rp => rp.RoleId == roleId.Value
                && rp.Permission.MasterCode == permissionMasterCode
                && rp.Permission.IsActive);

        if (hasPermission)
            return true;

        // Transitional fallback
        var anyPermissionsSeeded = await _dbContext.Set<RolePermission>().AnyAsync();
        if (!anyPermissionsSeeded)
        {
            var adminRoles = new[]
            {
                (int)RoleEnum.GroupAdmin
            };
            return adminRoles.Contains(roleId.Value);
        }

        return false;
    }

    public async Task<List<Guid>> GetGroupIdsWhereUserHasPermission(
        Guid userId,
        string permissionMasterCode,
        CancellationToken cancellationToken = default)
    {
        var groupIds = await _dbContext.GroupMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .Where(m => _dbContext.Set<RolePermission>()
                .Any(rp => rp.RoleId == m.RoleId
                    && rp.Permission.MasterCode == permissionMasterCode
                    && rp.Permission.IsActive))
            .Select(m => m.GroupTenant.GroupId)
            .ToListAsync(cancellationToken);

        if (groupIds.Count > 0)
            return groupIds;

        var anyPermissionsSeeded = await _dbContext.Set<RolePermission>().AnyAsync(cancellationToken);
        if (!anyPermissionsSeeded)
        {
            var adminRoles = new[]
            {
                (int)RoleEnum.GroupAdmin
            };

            return await _dbContext.GroupMembers
                .AsNoTracking()
                .Where(m => m.UserId == userId && adminRoles.Contains(m.RoleId))
                .Select(m => m.GroupTenant.GroupId)
                .ToListAsync(cancellationToken);
        }

        return groupIds;
    }

    public async Task<List<GroupMember>> GetMembershipsByUser(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.GroupMembers
            .AsNoTracking()
            .Include(m => m.GroupTenant)
                .ThenInclude(p => p.ApprovalStatus)
            .Include(m => m.GroupTenant)
                .ThenInclude(p => p.Group)
                    .ThenInclude(g => g.Actor)
            .Include(m => m.Role)
            .Where(m => m.UserId == userId)
            .ToListAsync(cancellationToken);
    }
}
