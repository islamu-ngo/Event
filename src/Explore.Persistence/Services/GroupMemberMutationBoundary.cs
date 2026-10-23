using System.Data.Common;
using System.IO;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Services;

/// <summary>Serializes ordinary group membership with native reviewer-independence qualification.</summary>
public sealed partial class GroupMemberMutationBoundary(
    ExploreDbContext context, IPrivacyIdentityFenceAuthority privacy, IUserContext user, IAdminContext admin,
    IMachinePrincipalAccessor machine, IAuthorizationProvider authorization, IGroupMemberRepository members)
    : IGroupMemberMutationBoundary, IGroupCreationMutationBoundary
{
    public Task<BaseCommandResponse<Guid>> ExecuteAddAsync(Guid tenantId, Guid groupId, Guid memberId, string email, int roleId,
        Func<Guid, Guid, CancellationToken, Task<BaseCommandResponse<Guid>>> operation, CancellationToken cancellationToken) =>
        ExecuteAsync(tenantId, groupId, memberId, email, roleId, AuthorizationActions.Create, operation, cancellationToken);

    public Task<BaseCommandResponse<Guid>> ExecuteRoleChangeAsync(Guid memberId, int roleId,
        Func<Guid, CancellationToken, Task<BaseCommandResponse<Guid>>> operation, CancellationToken cancellationToken) =>
        ExecuteAsync(null, null, memberId, null, roleId, AuthorizationActions.Update,
            (actor, _, token) => operation(actor, token), cancellationToken);

    public Task<BaseCommandResponse<Guid>> ExecuteRemovalAsync(Guid memberId,
        Func<Guid, CancellationToken, Task<BaseCommandResponse<Guid>>> operation, CancellationToken cancellationToken) =>
        ExecuteAsync(null, null, memberId, null, null, AuthorizationActions.Delete,
            (actor, _, token) => operation(actor, token), cancellationToken);

    private async Task<BaseCommandResponse<Guid>> ExecuteAsync(Guid? requestedTenant, Guid? groupId, Guid memberId,
        string? email, int? roleId, string action,
        Func<Guid, Guid, CancellationToken, Task<BaseCommandResponse<Guid>>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var tenantId = context.TenantFilterTenantId ?? Guid.Empty;
        RequireContext();
        if (memberId == Guid.Empty || requestedTenant is { } requested && requested != tenantId
            || context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges()) Deny();
        var permission = action switch
        {
            AuthorizationActions.Create => PermissionCodes.GroupMemberCreate,
            AuthorizationActions.Update => PermissionCodes.GroupMemberUpdate,
            AuthorizationActions.Delete => PermissionCodes.GroupMemberDelete,
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        Attempt? attempted = null;
        try
        {
            return await EfCoreUnitOfWork.ExecuteBootstrapConflictRetryAsync(() =>
                context.Database.CreateExecutionStrategy().ExecuteAsync<object?, BaseCommandResponse<Guid>>(null,
                async (_, _, token) =>
                {
                    RequireContext();
                    if (await VerifyAsync(token)) return attempted!.Result;
                    var actor = await admin.ResolveUserIdAsync(token);
                    if (actor is null || actor == Guid.Empty) Deny();
                    var target = groupId is { } group
                        ? await context.GroupTenants.AsNoTracking().Where(row => row.TenantId == tenantId && row.GroupId == group)
                            .Select(row => new Target(row.Id, row.GroupId, Guid.Empty)).SingleOrDefaultAsync(token)
                        : await context.GroupMembers.AsNoTracking().Where(row => row.Id == memberId && row.TenantId == tenantId)
                            .Select(row => new Target(row.GroupTenantId, row.GroupTenant.GroupId, row.UserId)).SingleOrDefaultAsync(token);
                    if (target is null) Deny();
                    var human = target!.HumanId;
                    if (groupId is not null)
                    {
                        var candidates = await context.Users.AsNoTracking().Where(row => row.Pii != null && row.Pii.Email == email)
                            .Select(row => row.Id).Take(2).ToArrayAsync(token);
                        if (candidates.Length > 1) Deny();
                        human = candidates.SingleOrDefault();
                    }
                    var decision = await authorization.AuthorizeAsync(new AuthorizationRequest(
                        AuthorizationCapabilityCatalog.Require(ResourceKinds.GroupMember, action), (groupId ?? memberId).ToString("D"),
                        Facts: new GroupMemberAuthorizationFacts(tenantId, target.GroupId,
                            groupId is null ? memberId : null, human == Guid.Empty ? null : human),
                        Subject: new AuthorizationSubject(actor, false), Tenant: new AuthorizationTenant(tenantId)), token);
                    if (!decision.IsAllowed)
                    {
                        if (decision.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
                            throw new AuthorizationProviderUnavailableException(ResourceKinds.GroupMember, action);
                        Deny();
                    }
                    context.ChangeTracker.Clear();
                    Guid? epochId = null;
                    long expectedRevision = 0;
                    async Task RecheckAsync(CancellationToken inner)
                    {
                        RequireContext();
                        if (await admin.ResolveUserIdAsync(inner) != actor
                            || await privacy.IsSubjectFencedAsync(actor!.Value, inner)
                            || !await context.Users.AsNoTracking().AnyAsync(row => row.Id == actor && !row.IsDeleted, inner)
                            || !await context.Tenants.AsNoTracking().AnyAsync(row => row.Id == tenantId && row.TenantStatus.IsActiveState, inner)
                            || !await context.GroupTenants.AsNoTracking().AnyAsync(row => row.Id == target.ParticipationId
                                && row.TenantId == tenantId && row.GroupId == target.GroupId && !row.Group.IsDeleted, inner)) Deny();
                        // Owned self-demotion/removal uses the permission fenced before this operation.
                        if (epochId is { } id && !await context.ModerationQualificationEpochs.AsNoTracking()
                            .AnyAsync(row => row.Id == id && row.Revision == expectedRevision, inner)) Deny();
                    }
                    return await privacy.ExecuteEnrollmentSerializedAsync(async privacyToken =>
                        await new EfCoreUnitOfWork(context).ExecuteRecognitionEnrollmentAsync(async inner =>
                        {
                            await using var installation = await RelationalNamedLock.AcquireTransactionAsync(
                                context, "moderation.authority-source.installation", inner);
                            if (groupId is not null)
                            {
                                var current = await context.Users.AsNoTracking().Where(row => row.Pii != null && row.Pii.Email == email)
                                    .Select(row => row.Id).Take(2).ToArrayAsync(inner);
                                if (current.Length > 1 || current.SingleOrDefault() != human) Deny();
                            }
                            if ((groupId is not null || roleId is not null) && human != Guid.Empty
                                && await privacy.IsSubjectFencedAsync(human, inner)) Deny();
                            var membershipRoles = await context.GroupMembers.AsNoTracking()
                                .Where(row => row.TenantId == tenantId && row.GroupTenantId == target.ParticipationId)
                                .Select(row => row.RoleId).Distinct().ToArrayAsync(inner);
                            await AcquireHumansAsync(tenantId, new[] { actor!.Value, human }.Where(id => id != Guid.Empty).Distinct().Order().ToArray(),
                                membershipRoles.Concat(roleId is { } requestedRole ? [requestedRole] : Array.Empty<int>()).ToArray(), permission, inner);
                            var epoch = await AcquireInstalledEpochAsync(tenantId, ResourceKinds.GroupMember, action, target.GroupId, inner);
                            if (epoch is not null)
                            {
                                epochId = epoch.Id;
                                expectedRevision = epoch.Revision;
                            }
                            await RelationalEntityRowFence.AcquireAsync<GroupTenant>(context, tenantId, row => row.Id, target.ParticipationId, inner);
                            var currentMembers = await context.GroupMembers.AsNoTracking().Where(row => row.TenantId == tenantId
                                && row.GroupTenantId == target.ParticipationId).OrderBy(row => row.Id).ToArrayAsync(inner);
                            foreach (var member in currentMembers)
                                await RelationalEntityRowFence.AcquireAsync<GroupMember>(context, tenantId, row => row.Id, member.Id, inner);
                            await RecheckAsync(inner);
                            var previous = await ReadAsync(inner);
                            if (groupId is null && (previous is null || previous.GroupTenantId != target.ParticipationId
                                || previous.UserId != human || previous.IsDeleted)) Deny();
                            if (!currentMembers.Any(row => row.UserId == actor && row.RoleId == (int)RoleEnum.GroupAdmin)
                                && !await members.HasPermissionInGroup(target.GroupId, actor.Value, permission)) Deny();
                            if (roleId is { } selectedRole && !await context.Roles.AsNoTracking().AnyAsync(
                                row => row.Id == selectedRole && row.RoleScopeId == (int)RoleScopeEnum.Group, inner)) Deny();
                            var result = await operation(actor.Value, human, inner);
                            var persisted = await ReadAsync(inner);
                            if (result.IsSuccess && persisted != previous)
                            {
                                if (persisted is null) throw Uncertain();
                                var beforeRevision = expectedRevision;
                                if (epoch is not null) { epoch.Advance(expectedRevision); expectedRevision = epoch.Revision; }
                                await context.SaveChangesAsync(inner);
                                attempted = new(actor.Value, result, previous, persisted, epochId, beforeRevision, expectedRevision);
                            }
                            return result;
                        }, RecheckAsync, privacyToken), RecheckAsync, token);
                }, async (_, _, token) => new ExecutionResult<BaseCommandResponse<Guid>>(
                    await VerifyAsync(token), attempted is null ? default! : attempted.Result), cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (attempted is not null && IsTransportFailure(exception))
        {
            if (await VerifyAsync(cancellationToken)) return attempted!.Result;
            throw;
        }

        async Task<bool> VerifyAsync(CancellationToken token)
        {
            if (attempted is not { } receipt) return false;
            RequireContext();
            if (await admin.ResolveUserIdAsync(token) != receipt.Actor) Deny();
            context.ChangeTracker.Clear();
            return await privacy.ExecuteEnrollmentSerializedAsync(async privacyToken =>
                await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async inner =>
                {
                    var actual = await ReadAsync(inner);
                    var revision = receipt.EpochId is { } id ? await context.ModerationQualificationEpochs.AsNoTracking()
                        .Where(row => row.Id == id).Select(row => (long?)row.Revision).SingleOrDefaultAsync(inner) : 0;
                    if (actual == receipt.After && revision == receipt.AfterRevision) return true;
                    if (actual == receipt.Before && revision == receipt.BeforeRevision) { attempted = null; return false; }
                    throw Uncertain();
                }, privacyToken), _ => Task.CompletedTask, token);
        }

        Task<MemberState?> ReadAsync(CancellationToken token) => context.GroupMembers.AsNoTracking().IgnoreQueryFilters(["SoftDelete"])
            .Where(row => row.Id == memberId && row.TenantId == tenantId)
            .Select(row => new MemberState(row.Id, row.TenantId, row.GroupTenantId, row.UserId, row.RoleId,
                row.GroupPositionId, row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy,
                row.IsDeleted, row.DeletedAt, row.DeletedBy)).SingleOrDefaultAsync(token);
        void RequireContext()
        {
            if (tenantId == Guid.Empty || !user.IsAuthenticated || machine.Current is not null
                || context.IsTenantFilterBypassed || context.TenantFilterTenantId != tenantId) Deny();
        }
        void Deny() => throw new AuthorizationException(ResourceKinds.GroupMember, action);
        static bool IsTransportFailure(Exception failure) => failure is DbException or TimeoutException or IOException
            || failure.InnerException is { } cause && IsTransportFailure(cause);
        static ConcurrencyConflictException Uncertain() => new(ConcurrencyConflictException.ConcurrentUpdate,
            "The group membership operation cannot be reconciled against its exact committed state.");
    }

    private async Task AcquireHumansAsync(Guid tenantId, Guid[] humans, int[] groupRoles, string permission, CancellationToken token)
    {
        foreach (var id in humans) await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, id, token);
        foreach (var id in await context.UserExternalLogins.AsNoTracking().Where(row => humans.Contains(row.UserId))
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token))
            await RelationalEntityRowFence.AcquireGlobalAsync<UserExternalLogin>(context, id, token);
        var platform = await context.PlatformUserRoles.AsNoTracking().Where(row => humans.Contains(row.UserId))
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId }).ToArrayAsync(token);
        foreach (var grant in platform) await RelationalEntityRowFence.AcquireGlobalAsync<PlatformUserRole>(context, grant.Id, token);
        var tenantMembers = await context.TenantUsers.AsNoTracking().Where(row => humans.Contains(row.UserId) && row.TenantId == tenantId)
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token);
        foreach (var id in tenantMembers) await RelationalEntityRowFence.AcquireAsync<TenantUser>(context, tenantId, row => row.Id, id, token);
        var grants = await context.TenantUserRoleGrants.AsNoTracking().Where(row => row.TenantId == tenantId && tenantMembers.Contains(row.TenantUserId))
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId }).ToArrayAsync(token);
        foreach (var grant in grants) await RelationalEntityRowFence.AcquireAsync<TenantUserRoleGrant>(context, tenantId, row => row.Id, grant.Id, token);
        foreach (var id in platform.Select(row => row.RoleId).Concat(grants.Select(row => row.RoleId)).Concat(groupRoles)
            .Append((int)RoleEnum.GroupAdmin).Distinct().Order())
            await RelationalEntityRowFence.AcquireLookupAsync<Role>(context, [id], token);
        var permissions = await context.Permissions.AsNoTracking().Where(row => row.MasterCode == permission)
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token);
        foreach (var id in permissions)
            await RelationalEntityRowFence.AcquireLookupAsync<Permission>(context, [id], token);
    }

    private sealed record Target(Guid ParticipationId, Guid GroupId, Guid HumanId);
    private sealed record MemberState(Guid Id, Guid TenantId, Guid GroupTenantId, Guid UserId, int RoleId,
        int? GroupPositionId, DateTime CreatedAt, Guid? CreatedBy, DateTime? UpdatedAt, Guid? UpdatedBy,
        bool IsDeleted, DateTime? DeletedAt, Guid? DeletedBy)
    { public override string ToString() => nameof(MemberState); }
    private sealed record Attempt(Guid Actor, BaseCommandResponse<Guid> Result, MemberState? Before, MemberState After,
        Guid? EpochId, long BeforeRevision, long AfterRevision)
    { public override string ToString() => nameof(Attempt); }
}
