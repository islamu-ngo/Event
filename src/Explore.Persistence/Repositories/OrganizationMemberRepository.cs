using System;
using System.Collections.Generic;
using System.Text;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class OrganizationMemberRepository : GenericRepository<OrganizationMember, Guid>, IOrganizationMemberRepository
{
    private readonly ExploreDbContext _dbContext;

    public OrganizationMemberRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<OrganizationMember>> GetOrganizationMembersWithDetails()
    {
        return await _dbContext.OrganizationMembers
            .AsNoTracking()
            .AsSplitQuery()
            .Include(m => m.User)
                .ThenInclude(u => u!.Pii)
            .Include(m => m.OrganizationTenant)
                .ThenInclude(p => p.Organization)
                    .ThenInclude(o => o.Pii)
            .Include(m => m.Role)
            .Include(m => m.OrganizationPosition)
            .ToListAsync();
    }

    public async Task<OrganizationMember?> GetOrganizationMemberWithDetails(Guid id)
    {
        return await _dbContext.OrganizationMembers
            .AsNoTracking()
            .AsSplitQuery()
            .Include(m => m.User)
                .ThenInclude(u => u!.Pii)
            .Include(m => m.OrganizationTenant)
                .ThenInclude(p => p.Organization)
                    .ThenInclude(o => o.Pii)
            .Include(m => m.OrganizationTenant)
                .ThenInclude(p => p.Organization)
                    .ThenInclude(o => o.Actor)
            .Include(m => m.Role)
            .Include(m => m.OrganizationPosition)
            .FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task<List<OrganizationMember>> GetMembersByOrganizationId(Guid organizationId)
    {
        return await _dbContext.OrganizationMembers
            .AsNoTracking()
            .AsSplitQuery()
            .Include(m => m.User)
                .ThenInclude(u => u!.Pii)
            .Include(m => m.Role)
            .Include(m => m.OrganizationPosition)
            .Include(m => m.OrganizationTenant)
                .ThenInclude(p => p.Organization)
                    .ThenInclude(o => o.Pii)
            .Where(m => m.OrganizationTenant.OrganizationId == organizationId)
            .ToListAsync();
    }

    public async Task<List<User>> GetUsersByOrganization(Guid organizationId)
    {
        return await _dbContext.OrganizationMembers
            .AsNoTracking()
            .Include(m => m.User)
                .ThenInclude(u => u!.Pii)
            .Where(m => m.OrganizationTenant.OrganizationId == organizationId)
            .Select(m => m.User)
            .ToListAsync();
    }

    public async Task<List<Organization>> GetOrganizationsByUser(Guid userId)
    {
        return await _dbContext.OrganizationMembers
            .AsNoTracking()
            .Include(m => m.OrganizationTenant)
                .ThenInclude(p => p.Organization)
                    .ThenInclude(o => o.Pii)
            .Where(m => m.UserId == userId)
            .Select(m => m.OrganizationTenant.Organization)
            .ToListAsync();
    }

    public async Task<List<OrganizationMember>> GetMembershipsByUser(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.OrganizationMembers
            .AsNoTracking()
            .Include(m => m.OrganizationTenant)
                .ThenInclude(p => p.ApprovalStatus)
            .Include(m => m.OrganizationTenant)
                .ThenInclude(p => p.Organization)
                    .ThenInclude(o => o.Pii)
            .Include(m => m.OrganizationTenant)
                .ThenInclude(p => p.Organization)
                    .ThenInclude(o => o.Actor)
            .Include(m => m.Role)
            .Where(m => m.UserId == userId)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> Exists(Guid organizationId, Guid userId)
    {
        return await _dbContext.OrganizationMembers
            .AsNoTracking()
            .AnyAsync(m => m.OrganizationTenant.OrganizationId == organizationId && m.UserId == userId);
    }

    public async Task<List<OrganizationMember>> GetInvitesByEmail(string email)
    {
        return await _dbContext.OrganizationMembers
            .AsNoTracking()
            .Include(m => m.OrganizationTenant)
                .ThenInclude(p => p.Organization)
                    .ThenInclude(o => o.Pii)
            .Include(m => m.User)
                .ThenInclude(u => u!.Pii)
            .Where(m => m.User.Pii != null && m.User.Pii.Email == email)
            .ToListAsync();
    }

    public async Task<OrganizationMember?> GetByOrganizationAndUser(Guid organizationId, Guid userId)
    {
        return await _dbContext.OrganizationMembers
            .AsNoTracking()
            .Include(m => m.Role)
            .Include(m => m.OrganizationTenant)
                .ThenInclude(p => p.Organization)
                    .ThenInclude(o => o.Pii)
            .FirstOrDefaultAsync(m => m.OrganizationTenant.OrganizationId == organizationId && m.UserId == userId);
    }

    public async Task<bool> HasPermissionInOrganization(Guid organizationId, Guid userId, string permissionMasterCode)
    {
        // Get the user's membership in this organization
        var roleId = await _dbContext.OrganizationMembers
            .AsNoTracking()
            .Where(m => m.OrganizationTenant.OrganizationId == organizationId && m.UserId == userId)
            .Select(m => (int?)m.RoleId)
            .FirstOrDefaultAsync();

        if (roleId == null)
            return false;

        // Permission-based check via RolePermission → Permission join
        var hasPermission = await _dbContext.Set<RolePermission>()
            .AsNoTracking()
            .AnyAsync(rp => rp.RoleId == roleId.Value
                && rp.Permission.MasterCode == permissionMasterCode
                && rp.Permission.IsActive);

        if (hasPermission)
            return true;

        // Transitional fallback: when RolePermission table has no data yet,
        // fall back to legacy admin role check. Remove once permissions are seeded.
        var anyPermissionsSeeded = await _dbContext.Set<RolePermission>().AnyAsync();
        if (!anyPermissionsSeeded)
        {
            var adminRoles = new[]
            {
                (int)RoleEnum.OrgAdmin
            };
            return adminRoles.Contains(roleId.Value);
        }

        return false;
    }

    public async Task<List<Guid>> GetOrganizationIdsWhereUserHasPermission(
        Guid userId,
        string permissionMasterCode,
        CancellationToken cancellationToken = default)
    {
        // Permission-based: find orgs where the user's role has the specified permission
        var orgIds = await _dbContext.OrganizationMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .Where(m => _dbContext.Set<RolePermission>()
                .Any(rp => rp.RoleId == m.RoleId
                    && rp.Permission.MasterCode == permissionMasterCode
                    && rp.Permission.IsActive))
            .Select(m => m.OrganizationTenant.OrganizationId)
            .ToListAsync(cancellationToken);

        if (orgIds.Count > 0)
            return orgIds;

        // Transitional fallback: when RolePermission table has no data yet
        var anyPermissionsSeeded = await _dbContext.Set<RolePermission>().AnyAsync(cancellationToken);
        if (!anyPermissionsSeeded)
        {
            var adminRoles = new[]
            {
                (int)RoleEnum.OrgAdmin
            };

            return await _dbContext.OrganizationMembers
                .AsNoTracking()
                .Where(m => m.UserId == userId && adminRoles.Contains(m.RoleId))
                .Select(m => m.OrganizationTenant.OrganizationId)
                .ToListAsync(cancellationToken);
        }

        return orgIds;
    }
}
