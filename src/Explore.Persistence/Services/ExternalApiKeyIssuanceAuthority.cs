using Explore.Application.Contracts.Persistence;
using Explore.Application.Authentication;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;

namespace Explore.Persistence.Services;

/// <summary>
/// Holds the actual user, ownership, membership and permission rows through the issuance commit.
/// </summary>
public sealed class ExternalApiKeyIssuanceAuthority(
    ExploreDbContext dbContext, IHttpContextAccessor httpContextAccessor) : IExternalApiKeyIssuanceAuthority
{
    public async Task<bool> IsAuthorizedForCommitAsync(
        Guid principalId, Guid? tenantId, ExternalApiKeyOwnerType ownerType, Guid ownerId,
        CancellationToken cancellationToken)
    {
        if (principalId == Guid.Empty || ownerId == Guid.Empty || !Enum.IsDefined(ownerType))
            return false;
        if (ownerType == ExternalApiKeyOwnerType.InstanceAdmin)
        {
            if (tenantId is not null || ownerId != principalId)
                return false;
        }
        else if (tenantId is not Guid activeTenantId || activeTenantId == Guid.Empty
            || dbContext.IsTenantFilterBypassed || dbContext.TenantFilterTenantId != activeTenantId)
        {
            return false;
        }

        await RelationalEntityRowFence.AcquireGlobalAsync<User>(dbContext, principalId, cancellationToken);
        if (!await dbContext.Users.AsNoTracking()
                .AnyAsync(user => user.Id == principalId && !user.IsDeleted, cancellationToken))
            return false;

        var accountKey = httpContextAccessor.HttpContext?.User.GetProviderIdentity()?.AccountKey;
        if (accountKey is not null)
        {
            Guid bindingId = await dbContext.UserExternalLogins.AsNoTracking()
                .Where(login => login.UserId == principalId
                    && login.AuthenticationProviderId == (int)accountKey.ProviderKind
                    && login.ProviderKey == accountKey.Value)
                .Select(login => login.Id).SingleOrDefaultAsync(cancellationToken);
            if (bindingId == Guid.Empty)
                return false;
            await RelationalEntityRowFence.AcquireGlobalAsync<UserExternalLogin>(
                dbContext, bindingId, cancellationToken);
            if (!await dbContext.UserExternalLogins.AsNoTracking().AnyAsync(login =>
                    login.Id == bindingId && login.UserId == principalId
                    && login.AuthenticationProviderId == (int)accountKey.ProviderKind
                    && login.ProviderKey == accountKey.Value, cancellationToken))
                return false;
        }

        if (ownerType == ExternalApiKeyOwnerType.InstanceAdmin)
        {
            var assignments = await dbContext.PlatformUserRoles.AsNoTracking()
                .Where(role => role.UserId == principalId && role.Role.RoleScopeId == (int)RoleScopeEnum.Platform
                    && role.Role.MasterCode == "platform.admin")
                .Select(role => new { role.Id, role.RoleId })
                .ToListAsync(cancellationToken);
            foreach (Guid id in assignments.Select(role => role.Id).Order())
                await RelationalEntityRowFence.AcquireGlobalAsync<PlatformUserRole>(dbContext, id, cancellationToken);
            int[] roleIds = assignments.Select(role => role.RoleId).Distinct().Order().ToArray();
            foreach (int id in roleIds)
                await RelationalEntityRowFence.AcquireLookupAsync<Role>(dbContext, [id], cancellationToken);
            Guid[] assignmentIds = assignments.Select(role => role.Id).ToArray();
            return await dbContext.PlatformUserRoles.AsNoTracking()
                .AnyAsync(role => assignmentIds.Contains(role.Id) && role.UserId == principalId
                    && roleIds.Contains(role.RoleId) && role.Role.RoleScopeId == (int)RoleScopeEnum.Platform
                    && role.Role.MasterCode == "platform.admin", cancellationToken);
        }

        Guid scope = tenantId!.Value;
        await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(dbContext, scope, cancellationToken);
        int? tenantStatusId = await dbContext.Tenants.AsNoTracking()
            .Where(tenant => tenant.Id == scope)
            .Select(tenant => (int?)tenant.TenantStatusId).SingleOrDefaultAsync(cancellationToken);
        if (tenantStatusId is not int statusId)
            return false;
        await RelationalEntityRowFence.AcquireLookupAsync<TenantStatus>(dbContext, [statusId], cancellationToken);
        if (!await dbContext.Tenants.AsNoTracking()
                .AnyAsync(tenant => tenant.Id == scope && tenant.TenantStatus.IsActiveState, cancellationToken))
            return false;

        if (ownerType is ExternalApiKeyOwnerType.User or ExternalApiKeyOwnerType.Tenant)
        {
            if (ownerType == ExternalApiKeyOwnerType.User && ownerId != principalId
                || ownerType == ExternalApiKeyOwnerType.Tenant && ownerId != scope)
                return false;
            Guid membershipId = await dbContext.TenantUsers.AsNoTracking()
                .Where(member => member.TenantId == scope && member.UserId == principalId
                    && !member.IsDeleted && member.StatusId == (int)TenantUserStatusEnum.Active)
                .Select(member => member.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (membershipId == Guid.Empty)
                return false;
            await RelationalEntityRowFence.AcquireAsync<TenantUser>(
                dbContext, scope, member => member.Id, membershipId, cancellationToken);
            if (!await dbContext.TenantUsers.AsNoTracking().AnyAsync(member =>
                    member.Id == membershipId && member.TenantId == scope && member.UserId == principalId
                    && !member.IsDeleted && member.StatusId == (int)TenantUserStatusEnum.Active, cancellationToken))
                return false;
            if (ownerType == ExternalApiKeyOwnerType.User)
                return true;

            Guid[] grantIds = await dbContext.TenantUserRoleGrants.AsNoTracking()
                .Where(grant => grant.TenantId == scope && grant.TenantUserId == membershipId
                    && grant.RoleId == (int)RoleEnum.TenantAdmin && grant.RevokedAt == null)
                .Select(grant => grant.Id)
                .ToArrayAsync(cancellationToken);
            foreach (Guid id in grantIds.Order())
                await RelationalEntityRowFence.AcquireAsync<TenantUserRoleGrant>(
                    dbContext, scope, grant => grant.Id, id, cancellationToken);
            await RelationalEntityRowFence.AcquireLookupAsync<Role>(
                dbContext, [(int)RoleEnum.TenantAdmin], cancellationToken);
            return await dbContext.TenantUserRoleGrants.AsNoTracking().AnyAsync(grant =>
                grantIds.Contains(grant.Id) && grant.TenantId == scope && grant.TenantUserId == membershipId
                && grant.RevokedAt == null && grant.RoleId == (int)RoleEnum.TenantAdmin
                && grant.RoleScopeId == (int)RoleScopeEnum.Tenant
                && grant.Role.RoleScopeId == (int)RoleScopeEnum.Tenant, cancellationToken);
        }

        if (ownerType == ExternalApiKeyOwnerType.Organization)
        {
            await RelationalEntityRowFence.AcquireGlobalAsync<Organization>(dbContext, ownerId, cancellationToken);
            if (!await dbContext.Organizations.AsNoTracking()
                    .AnyAsync(owner => owner.Id == ownerId && !owner.IsDeleted, cancellationToken))
                return false;
            Guid placementId = await dbContext.OrganizationTenants.AsNoTracking()
                .Where(placement => placement.TenantId == scope && placement.OrganizationId == ownerId && !placement.IsDeleted)
                .Select(placement => placement.Id).SingleOrDefaultAsync(cancellationToken);
            if (placementId == Guid.Empty)
                return false;
            await RelationalEntityRowFence.AcquireAsync<OrganizationTenant>(
                dbContext, scope, placement => placement.Id, placementId, cancellationToken);
            if (!await dbContext.OrganizationTenants.AsNoTracking().AnyAsync(placement =>
                    placement.Id == placementId && placement.TenantId == scope
                    && placement.OrganizationId == ownerId && !placement.IsDeleted
                    && !placement.IsSuspended, cancellationToken))
                return false;
            Guid memberId = await dbContext.OrganizationMembers.AsNoTracking()
                .Where(member => member.TenantId == scope && member.OrganizationTenantId == placementId
                    && member.UserId == principalId && !member.IsDeleted)
                .Select(member => member.Id).SingleOrDefaultAsync(cancellationToken);
            if (memberId == Guid.Empty)
                return false;
            await RelationalEntityRowFence.AcquireAsync<OrganizationMember>(
                dbContext, scope, member => member.Id, memberId, cancellationToken);
            int? roleId = await dbContext.OrganizationMembers.AsNoTracking()
                .Where(member => member.Id == memberId && member.TenantId == scope
                    && member.OrganizationTenantId == placementId && member.UserId == principalId && !member.IsDeleted)
                .Select(member => (int?)member.RoleId).SingleOrDefaultAsync(cancellationToken);
            return roleId is int role && await FenceManagementPermissionAsync(
                role, RoleScopeEnum.Organization, PermissionCodes.OrganizationManage, cancellationToken);
        }

        if (ownerType == ExternalApiKeyOwnerType.Group)
        {
            await RelationalEntityRowFence.AcquireGlobalAsync<Group>(dbContext, ownerId, cancellationToken);
            if (!await dbContext.Groups.AsNoTracking()
                    .AnyAsync(owner => owner.Id == ownerId && !owner.IsDeleted, cancellationToken))
                return false;
            Guid placementId = await dbContext.GroupTenants.AsNoTracking()
                .Where(placement => placement.TenantId == scope && placement.GroupId == ownerId && !placement.IsDeleted)
                .Select(placement => placement.Id).SingleOrDefaultAsync(cancellationToken);
            if (placementId == Guid.Empty)
                return false;
            await RelationalEntityRowFence.AcquireAsync<GroupTenant>(
                dbContext, scope, placement => placement.Id, placementId, cancellationToken);
            if (!await dbContext.GroupTenants.AsNoTracking().AnyAsync(placement =>
                    placement.Id == placementId && placement.TenantId == scope
                    && placement.GroupId == ownerId && !placement.IsDeleted
                    && !placement.IsSuspended, cancellationToken))
                return false;
            Guid memberId = await dbContext.GroupMembers.AsNoTracking()
                .Where(member => member.TenantId == scope && member.GroupTenantId == placementId
                    && member.UserId == principalId && !member.IsDeleted)
                .Select(member => member.Id).SingleOrDefaultAsync(cancellationToken);
            if (memberId == Guid.Empty)
                return false;
            await RelationalEntityRowFence.AcquireAsync<GroupMember>(
                dbContext, scope, member => member.Id, memberId, cancellationToken);
            int? roleId = await dbContext.GroupMembers.AsNoTracking()
                .Where(member => member.Id == memberId && member.TenantId == scope
                    && member.GroupTenantId == placementId && member.UserId == principalId && !member.IsDeleted)
                .Select(member => (int?)member.RoleId).SingleOrDefaultAsync(cancellationToken);
            return roleId is int role && await FenceManagementPermissionAsync(
                role, RoleScopeEnum.Group, PermissionCodes.GroupManage, cancellationToken);
        }

        return false;
    }

    private async Task<bool> FenceManagementPermissionAsync(
        int roleId, RoleScopeEnum scope, string permissionCode, CancellationToken cancellationToken)
    {
        await RelationalEntityRowFence.AcquireLookupAsync<Role>(dbContext, [roleId], cancellationToken);
        if (!await dbContext.Roles.AsNoTracking()
                .AnyAsync(role => role.Id == roleId && role.RoleScopeId == (int)scope, cancellationToken))
            return false;
        int[] permissionIds = await dbContext.RolePermissions.AsNoTracking()
            .Where(mapping => mapping.RoleId == roleId && mapping.Permission.MasterCode == permissionCode
                && mapping.Permission.IsActive && mapping.Permission.RoleScopeId == (int)scope)
            .Select(mapping => mapping.PermissionId).ToArrayAsync(cancellationToken);
        foreach (int id in permissionIds.Order())
        {
            await RelationalEntityRowFence.AcquireLookupAsync<Permission>(dbContext, [id], cancellationToken);
            await RelationalEntityRowFence.AcquireLookupAsync<RolePermission>(dbContext, [roleId, id], cancellationToken);
        }
        return await dbContext.RolePermissions.AsNoTracking().AnyAsync(mapping =>
            mapping.RoleId == roleId && permissionIds.Contains(mapping.PermissionId)
            && mapping.Permission.MasterCode == permissionCode && mapping.Permission.IsActive
            && mapping.Permission.RoleScopeId == (int)scope, cancellationToken);
    }
}
