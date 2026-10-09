using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>Actual native qualification sources acquired on the owning transaction's connection.</summary>
internal sealed class ModerationQualificationAuthority(
    ExploreDbContext context, IAdminContext admin, IMachinePrincipalAccessor machine)
{
    public Task<ModerationSourceAuthority?> AcquireAsync(
        ModerationQualificationMutation mutation, CancellationToken token) =>
        AcquireAsync(mutation.Target, mutation is RecordModerationCaseResponse,
            mutation is not RecordModerationCaseResponse, mutation, token);

    public Task<ModerationSourceAuthority?> AcquireAsync(
        AuthorityScope target, CancellationToken token) =>
        AcquireAsync(target, false, true, null, token);

    private async Task<ModerationSourceAuthority?> AcquireAsync(
        AuthorityScope target, bool allowInactiveTenant, bool requireAdministrator,
        ModerationQualificationMutation? mutation, CancellationToken token)
    {
        if (machine.IsMachineCaller || context.Database.CurrentTransaction is null
            || context.IsTenantFilterBypassed || context.TenantFilterTenantId is not { } tenantId
            || tenantId == Guid.Empty || !await ProviderTenantMatchesAsync(tenantId, token))
            return null;

        var userId = await admin.ResolveUserIdAsync(token);
        var discoveredBinding = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        if (userId is null || discoveredBinding is null)
            return null;

        // This bounded discovery scope is provenance, not permission. Every fact used below
        // is subsequently fenced and reread before an authority result is returned.
        context.ModerationTrustDomainId = discoveredBinding.TrustDomainId;
        var humans = new HashSet<Guid> { userId.Value };
        var requiredHumans = new HashSet<Guid> { userId.Value };
        switch (mutation)
        {
            case OpenModerationCaseSource { SubjectKind: ModerationSubjectKind.Human } source:
                humans.Add(source.SubjectId);
                requiredHumans.Add(source.SubjectId);
                break;
            case AssignModerationReviewerSource source:
                humans.Add(source.ReviewerHumanId);
                requiredHumans.Add(source.ReviewerHumanId);
                break;
            case QualifyModerationReviewerController source:
                humans.Add(source.ControllerHumanId);
                requiredHumans.Add(source.ControllerHumanId);
                await DiscoverReviewerAsync(source.AssignmentId, humans, requiredHumans, true, false, token);
                break;
            case AssessModerationReviewerConflict source:
                await DiscoverReviewerAsync(source.AssignmentId, humans, requiredHumans, true, true, token);
                break;
            case WithdrawModerationReviewerSource source:
                await DiscoverReviewerAsync(source.AssignmentId, humans, requiredHumans, false, false, token);
                break;
            case FinalizeModerationFindingSource source:
                var assignment = await context.ModerationReviewerAssignments.AsNoTracking()
                    .SingleOrDefaultAsync(row => row.CaseId == source.CaseId
                        && row.ReviewerHumanId == userId && row.WithdrawnAt == null, token);
                if (assignment is not null)
                    await DiscoverReviewerAsync(assignment.Id, humans, requiredHumans, true, true, token);
                break;
        }

        foreach (var id in humans.Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, id, token);
        var humanIds = humans.ToArray();
        var requiredIds = requiredHumans.ToArray();
        if (await context.Users.AsNoTracking().CountAsync(row => requiredIds.Contains(row.Id) && !row.IsDeleted, token)
            != requiredIds.Length)
            return null;

        var logins = await context.UserExternalLogins.AsNoTracking().Where(row => humanIds.Contains(row.UserId))
            .OrderBy(row => row.Id).Select(row => row.Id).ToListAsync(token);
        foreach (var id in logins)
            await RelationalEntityRowFence.AcquireGlobalAsync<UserExternalLogin>(context, id, token);

        var platformGrants = await context.PlatformUserRoles.AsNoTracking().Where(row => humanIds.Contains(row.UserId))
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId }).ToListAsync(token);
        foreach (var grant in platformGrants)
            await RelationalEntityRowFence.AcquireGlobalAsync<PlatformUserRole>(context, grant.Id, token);

        var memberships = await context.TenantUsers.AsNoTracking()
            .Where(row => humanIds.Contains(row.UserId) && row.TenantId == tenantId)
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.StatusId }).ToListAsync(token);
        foreach (var membership in memberships)
            await RelationalEntityRowFence.AcquireAsync<TenantUser>(context, tenantId, row => row.Id, membership.Id, token);
        var membershipIds = memberships.Select(row => row.Id).ToArray();
        var tenantGrants = await context.TenantUserRoleGrants.AsNoTracking()
            .Where(row => membershipIds.Contains(row.TenantUserId))
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId }).ToListAsync(token);
        foreach (var grant in tenantGrants)
            await RelationalEntityRowFence.AcquireAsync<TenantUserRoleGrant>(context, tenantId, row => row.Id, grant.Id, token);
        var roleIds = platformGrants.Select(row => row.RoleId).Concat(tenantGrants.Select(row => row.RoleId))
            .Distinct().Order().ToArray();
        if (roleIds.Length > 0)
            await RelationalEntityRowFence.AcquireLookupAsync<Role>(context, roleIds, token);
        if (await admin.ResolveUserIdAsync(token) != userId)
            return null;

        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationHostBinding>(context, discoveredBinding.Id, token);
        var binding = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        if (binding is null || binding.Id != discoveredBinding.Id
            || binding.TrustDomainId != discoveredBinding.TrustDomainId
            || binding.SolutionId != discoveredBinding.SolutionId || binding.InstanceId != discoveredBinding.InstanceId)
            return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ServicePrincipal>(context, binding.MaintenanceServicePrincipalId, token);
        if (!await context.ServicePrincipals.AsNoTracking().AnyAsync(
            row => row.Id == binding.MaintenanceServicePrincipalId && !row.IsDeleted, token))
            return null;

        await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, tenantId, token);
        var tenant = await context.Tenants.AsNoTracking().SingleOrDefaultAsync(row => row.Id == tenantId, token);
        if (tenant is null)
            return null;
        await RelationalEntityRowFence.AcquireLookupAsync<TenantStatus>(context, [tenant.TenantStatusId], token);
        if (!await context.Tenants.AsNoTracking().AnyAsync(
            row => row.Id == tenantId && (row.TenantStatus.IsActiveState || allowInactiveTenant), token))
            return null;
        var discoveredEpoch = await context.ModerationQualificationEpochs.AsNoTracking()
            .SingleOrDefaultAsync(row => row.HostBindingId == binding.Id && row.TenantId == tenantId, token);
        if (discoveredEpoch is null)
            return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEpoch>(context, discoveredEpoch.Id, token);
        var epoch = await context.ModerationQualificationEpochs
            .SingleOrDefaultAsync(row => row.Id == discoveredEpoch.Id, token);
        if (epoch is null || epoch.Revision < 1)
            return null;

        var instance = AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(binding.TrustDomainId), binding.SolutionId),
            binding.InstanceId);
        var tenantScope = AuthorityScope.Tenant(instance, tenantId);
        var platformIds = platformGrants.Select(row => row.Id).ToArray();
        var instanceAdministrators = await context.PlatformUserRoles.AsNoTracking().Where(row =>
            platformIds.Contains(row.Id) && roleIds.Contains(row.RoleId) && !row.User.IsDeleted
            && row.Role.RoleScopeId == (int)RoleScopeEnum.Platform && row.Role.MasterCode == "platform.admin")
            .Select(row => row.UserId).Distinct().ToListAsync(token);
        var isInstanceAdmin = instanceAdministrators.Contains(userId.Value);
        var tenantIds = tenantGrants.Select(row => row.Id).ToArray();
        var tenantAdministrators = await context.TenantUserRoleGrants.AsNoTracking().Where(row =>
            tenantIds.Contains(row.Id) && roleIds.Contains(row.RoleId) && membershipIds.Contains(row.TenantUserId)
            && row.TenantId == tenantId && row.RevokedAt == null && row.RoleScopeId == (int)RoleScopeEnum.Tenant
            && row.TenantUser.StatusId == (int)TenantUserStatusEnum.Active
            && !row.TenantUser.IsDeleted && !row.TenantUser.User.IsDeleted
            && row.RoleId == (int)RoleEnum.TenantAdmin && row.Role.RoleScopeId == (int)RoleScopeEnum.Tenant)
            .Select(row => row.TenantUser.UserId).Distinct().ToListAsync(token);
        var isTenantAdmin = tenantAdministrators.Contains(userId.Value);
        if (!(mutation is null ? tenantScope.Contains(target) : target == tenantScope)
            && (target != instance || !isInstanceAdmin && requireAdministrator))
            return null;
        if (requireAdministrator && !isInstanceAdmin && !isTenantAdmin)
            return null;

        var administrators = target == instance ? instanceAdministrators.ToArray()
            : instanceAdministrators.Concat(tenantAdministrators).Distinct().ToArray();
        return new ModerationSourceAuthority(userId.Value, binding, epoch, instance, tenantScope,
            isInstanceAdmin, humanIds, administrators);
    }

    private async Task DiscoverReviewerAsync(
        Guid assignmentId, HashSet<Guid> humans, HashSet<Guid> requiredHumans,
        bool requireReviewer, bool requireController, CancellationToken token)
    {
        var assignment = await context.ModerationReviewerAssignments.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == assignmentId, token);
        if (assignment is null) return;
        humans.Add(assignment.ReviewerHumanId);
        if (requireReviewer)
            requiredHumans.Add(assignment.ReviewerHumanId);
        if (assignment.ControllerHumanId is { } controller)
        {
            humans.Add(controller);
            if (requireController)
                requiredHumans.Add(controller);
        }
        if (requireController)
        {
            foreach (var assessor in new[] { assignment.QualifiedByHumanId, assignment.AssessedByHumanId })
            {
                if (assessor is not { } id) continue;
                humans.Add(id);
                requiredHumans.Add(id);
            }
        }
    }

    public async Task<bool> ProviderTenantMatchesAsync(Guid tenantId, CancellationToken token)
    {
        if (context.TenantFilterTenantId != tenantId || context.IsTenantFilterBypassed)
            return false;
        if (context.Database.ProviderName != RelationalNamedLock.PostgreSqlProvider)
            return true;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT current_setting('app.current_tenant_id', true)";
        var value = await command.ExecuteScalarAsync(token);
        return Guid.TryParse(value?.ToString(), out var actual) && actual == tenantId;
    }
}

internal sealed record ModerationSourceAuthority(
    Guid UserId, ModerationHostBinding Binding, ModerationQualificationEpoch Epoch,
    AuthorityScope Instance, AuthorityScope Tenant, bool IsInstanceAdmin,
    IReadOnlyList<Guid> FencedHumans, IReadOnlyList<Guid> Administrators);
