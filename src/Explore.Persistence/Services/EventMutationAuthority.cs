using System.Data;
using Explore.Application.Authorization;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.AspNetCore.Http;

namespace Explore.Persistence.Services;

internal sealed class EventMutationAuthority(ExploreDbContext dbContext, TimeProvider? timeProvider)
{
    internal Task<Event?> ForPublicActionsAsync(
        Guid tenantId, Guid userId, Guid eventId, CancellationToken cancellationToken) =>
        ResolveAsync(tenantId, userId, eventId, PermissionCodes.EventManagePublicActions,
            allowTenantAdministration: true, cancellationToken);

    internal Task<Event?> ForUpdateAsync(
        Guid tenantId, Guid userId, Guid eventId, CancellationToken cancellationToken) =>
        ResolveAsync(tenantId, userId, eventId, PermissionCodes.EventUpdate,
            allowTenantAdministration: false, cancellationToken);

    internal Task<Event?> ForDeleteAsync(
        Guid tenantId, Guid userId, Guid eventId, CancellationToken cancellationToken) =>
        ResolveAsync(tenantId, userId, eventId, PermissionCodes.EventDelete,
            allowTenantAdministration: false, cancellationToken);

    internal Task<Event?> ForAgendaItemAsync(
        Guid tenantId, Guid userId, Guid eventId, string action,
        IHttpContextAccessor httpContext, CancellationToken cancellationToken) =>
        ForOperationalChildAsync(tenantId, userId, eventId, action switch
        {
            AuthorizationActions.Create => PermissionCodes.EventAgendaItemCreate,
            AuthorizationActions.Update => PermissionCodes.EventAgendaItemUpdate,
            AuthorizationActions.Delete => PermissionCodes.EventAgendaItemDelete,
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        }, httpContext, cancellationToken);

    internal Task<Event?> ForSessionAsync(
        Guid tenantId, Guid userId, Guid eventId, string action,
        IHttpContextAccessor httpContext, CancellationToken cancellationToken) =>
        ForOperationalChildAsync(tenantId, userId, eventId, action switch
        {
            AuthorizationActions.Create => PermissionCodes.EventSessionCreate,
            AuthorizationActions.Update => PermissionCodes.EventSessionUpdate,
            AuthorizationActions.Delete => PermissionCodes.EventSessionDelete,
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        }, httpContext, cancellationToken);

    private async Task<Event?> ForOperationalChildAsync(
        Guid tenantId, Guid userId, Guid eventId, string permission,
        IHttpContextAccessor httpContext, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty || eventId == Guid.Empty
            || dbContext.IsTenantFilterBypassed || dbContext.TenantFilterTenantId != tenantId)
            return null;
        if (dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Event child mutation authority requires the caller's Serializable transaction.");
        var principal = new ExternalApiKeyIssuanceAuthority(dbContext, httpContext);
        bool instanceAdministrator = await principal.IsInstanceAdministratorForCommitAsync(userId, cancellationToken);
        if (!instanceAdministrator && !await principal.IsAuthorizedForCommitAsync(
                userId, tenantId, ExternalApiKeyOwnerType.User, userId, cancellationToken))
            return null;
        return await ResolveAsync(tenantId, userId, eventId, permission,
            allowTenantAdministration: true, cancellationToken, instanceAdministrator);
    }

    private async Task<Event?> ResolveAsync(
        Guid tenantId, Guid userId, Guid eventId, string permissionCode,
        bool allowTenantAdministration, CancellationToken cancellationToken, bool instanceAdministrator = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (tenantId == Guid.Empty || userId == Guid.Empty || eventId == Guid.Empty
            || dbContext.IsTenantFilterBypassed || dbContext.TenantFilterTenantId != tenantId)
            return null;
        if (dbContext.Database.IsRelational()
            && dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Event mutation authority requires the caller's Serializable transaction.");

        // The subject, contributing memberships and role rows are database evidence.
        // Neither a claimed publisher nor a prior decorator decision grants this operation.
        if (!instanceAdministrator && !await new TenantUserRepository(dbContext)
                .FenceActiveTenantUserAsync(tenantId, userId, cancellationToken))
            return null;
        var authority = new EventAuthoritySnapshotService(dbContext);
        await authority.GetCommitBoundForUserAndEventsAsync(
            tenantId, userId, [eventId], (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime,
            cancellationToken);
        var target = await dbContext.Events.AsNoTracking()
            .Include(row => row.Actor).Include(row => row.OrganizerActor)
            .Include(row => row.ParticipationConfiguration)
            .SingleOrDefaultAsync(row => row.Id == eventId && row.TenantId == tenantId, cancellationToken);
        if (target is null || target.IsDeleted || target.Actor.IsDeleted || target.Actor.IsSuspended)
            return null;
        int? tenantStatusId = await dbContext.Tenants.AsNoTracking()
            .Where(tenant => tenant.Id == tenantId).Select(tenant => (int?)tenant.TenantStatusId)
            .SingleOrDefaultAsync(cancellationToken);
        if (tenantStatusId is not { } statusId)
            return null;
        await RelationalEntityRowFence.AcquireLookupAsync<TenantStatus>(dbContext, [statusId], cancellationToken);
        if (!await dbContext.Tenants.AsNoTracking().AnyAsync(
                tenant => tenant.Id == tenantId && tenant.TenantStatus.IsActiveState, cancellationToken))
            return null;

        if (instanceAdministrator)
            return target;

        // Only the fixed child policies admit instance administration. Event update and
        // public-action callers must independently hold one of the exact grants below.
        if (allowTenantAdministration)
        {
            var tenantGrants = await dbContext.TenantUserRoleGrants.AsNoTracking()
                .Where(row => row.TenantId == tenantId && row.TenantUser.UserId == userId
                    && row.RoleId == (int)RoleEnum.TenantAdmin && row.RevokedAt == null)
                .Select(row => row.Id).ToListAsync(cancellationToken);
            foreach (Guid id in tenantGrants.Order())
                await RelationalEntityRowFence.AcquireAsync<TenantUserRoleGrant>(
                    dbContext, tenantId, row => row.Id, id, cancellationToken);
            await RelationalEntityRowFence.AcquireLookupAsync<Role>(
                dbContext, [(int)RoleEnum.TenantAdmin], cancellationToken);
            if (await dbContext.TenantUserRoleGrants.AsNoTracking().AnyAsync(
                    row => row.TenantId == tenantId && row.TenantUser.UserId == userId
                        && row.TenantUser.StatusId == (int)TenantUserStatusEnum.Active && !row.TenantUser.IsDeleted
                        && row.RoleId == (int)RoleEnum.TenantAdmin && row.RevokedAt == null
                        && row.RoleScopeId == (int)RoleScopeEnum.Tenant
                        && row.Role.RoleScopeId == (int)RoleScopeEnum.Tenant, cancellationToken))
                return target;
        }

        if (target.Actor.UserId == userId)
            return target;

        if (target.Actor.OrganizationId is { } organizationId)
        {
            await RelationalEntityRowFence.AcquireGlobalAsync<Organization>(dbContext, organizationId, cancellationToken);
            var placements = await dbContext.OrganizationTenants.AsNoTracking()
                .Where(row => row.TenantId == tenantId && row.OrganizationId == organizationId)
                .Select(row => row.Id).ToListAsync(cancellationToken);
            foreach (Guid id in placements.Order())
                await RelationalEntityRowFence.AcquireAsync<OrganizationTenant>(
                    dbContext, tenantId, row => row.Id, id, cancellationToken);
            var members = await dbContext.OrganizationMembers.AsNoTracking()
                .Where(row => row.TenantId == tenantId && row.UserId == userId
                    && row.OrganizationTenant.OrganizationId == organizationId)
                .Select(row => row.Id).ToListAsync(cancellationToken);
            foreach (Guid id in members.Order())
                await RelationalEntityRowFence.AcquireAsync<OrganizationMember>(
                    dbContext, tenantId, row => row.Id, id, cancellationToken);
            await RelationalEntityRowFence.AcquireLookupAsync<Role>(dbContext, [(int)RoleEnum.OrgAdmin], cancellationToken);
            if (await dbContext.OrganizationMembers.AsNoTracking().AnyAsync(
                    row => row.TenantId == tenantId && row.UserId == userId && row.RoleId == (int)RoleEnum.OrgAdmin
                        && row.Role.RoleScopeId == (int)RoleScopeEnum.Organization
                        && row.OrganizationTenant.OrganizationId == organizationId
                        && !row.OrganizationTenant.IsDeleted && !row.OrganizationTenant.IsSuspended
                        && !row.OrganizationTenant.Organization.IsDeleted, cancellationToken))
                return target;
        }

        // Evaluate expiry after the provider response and every native fence wait.
        var current = await authority.GetForUserAndEventsAsync(tenantId, userId, [eventId],
            (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime, cancellationToken);
        return current.Events.TryGetValue(eventId, out var grant)
            && grant.PermissionCodes.Contains(permissionCode) ? target : null;
    }
}
