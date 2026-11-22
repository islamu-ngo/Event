using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class AdministrativeAuthorityQuery
{
    private const string EnumerationReason =
        "Administrative authority enumeration selects primitive scopes for one exact internal user.";

    internal static async Task<bool> IsInstanceAdminAsync(
        ExploreDbContext context, Guid userId, CancellationToken cancellationToken)
    {
        return await context.PlatformUserRoles.AsNoTracking().AnyAsync(row => row.UserId == userId
            && row.Role.RoleScopeId == (int)RoleScopeEnum.Platform && row.Role.MasterCode == "platform.admin",
            cancellationToken);
    }

    internal static async Task<IReadOnlyList<Guid>> GetAdminTenantIdsAsync(
        ExploreDbContext context, Guid userId, CancellationToken cancellationToken)
    {
        return await context.TenantUserRoleGrants.IgnoreTenantFilter(EnumerationReason).AsNoTracking()
            .Where(row => row.TenantUser.UserId == userId && row.RevokedAt == null
                && row.TenantId == row.TenantUser.TenantId
                && row.RoleId == (int)RoleEnum.TenantAdmin
                && row.TenantUser.StatusId == (int)TenantUserStatusEnum.Active && !row.TenantUser.IsDeleted)
            .Select(row => row.TenantId).Distinct().ToListAsync(cancellationToken);
    }

    internal static async Task<IReadOnlyList<Guid>> GetAdminOrganizationIdsAsync(
        ExploreDbContext context, Guid userId, Guid? tenantId, CancellationToken cancellationToken)
    {
        return await context.OrganizationMembers.IgnoreTenantFilter(EnumerationReason).AsNoTracking()
            .Where(row => row.UserId == userId && row.RoleId == (int)RoleEnum.OrgAdmin
                && row.TenantId == row.OrganizationTenant.TenantId
                && (!tenantId.HasValue || row.TenantId == tenantId)
                && !row.OrganizationTenant.IsDeleted && !row.OrganizationTenant.Organization.IsDeleted)
            .Select(row => row.OrganizationTenant.OrganizationId).Distinct().ToListAsync(cancellationToken);
    }

    internal static async Task<IReadOnlyList<Guid>> GetAdminGroupIdsAsync(
        ExploreDbContext context, Guid userId, Guid? tenantId, CancellationToken cancellationToken)
    {
        return await context.GroupMembers.IgnoreTenantFilter(EnumerationReason).AsNoTracking()
            .Where(row => row.UserId == userId && row.RoleId == (int)RoleEnum.GroupAdmin
                && row.TenantId == row.GroupTenant.TenantId
                && (!tenantId.HasValue || row.TenantId == tenantId)
                && !row.GroupTenant.IsDeleted && !row.GroupTenant.Group.IsDeleted)
            .Select(row => row.GroupTenant.GroupId).Distinct().ToListAsync(cancellationToken);
    }
}
