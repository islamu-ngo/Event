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
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Services;

/// <summary>Serializes organization membership with native reviewer-independence qualification.</summary>
public sealed partial class OrganizationMemberMutationBoundary(
    ExploreDbContext context, IPrivacyIdentityFenceAuthority privacy, IUserContext user,
    IAdminContext admin, IMachinePrincipalAccessor machine, IAuthorizationProvider authorization,
    IHumanActionCommitFinalizer finalizer)
    : IOrganizationMemberMutationBoundary, IOrganizationCreationMutationBoundary
{
    public Task<BaseCommandResponse<Guid>> ExecuteAddAsync(Guid tenantId, Guid organizationId, Guid memberId, string email, int roleId,
        Func<Guid, Guid, CancellationToken, Task<BaseCommandResponse<Guid>>> operation, CancellationToken cancellationToken) =>
        ExecuteAsync(tenantId, organizationId, memberId, email, roleId, false, AuthorizationActions.Create, operation, cancellationToken);

    public Task<BaseCommandResponse<Guid>> ExecuteRoleChangeAsync(Guid memberId, int roleId,
        Func<Guid, CancellationToken, Task<BaseCommandResponse<Guid>>> operation, CancellationToken cancellationToken) =>
        ExecuteAsync(null, null, memberId, null, roleId, false, AuthorizationActions.Update,
            (actor, _, token) => operation(actor, token), cancellationToken);

    public Task<BaseCommandResponse<Guid>> ExecuteRemovalAsync(Guid memberId, bool recipientDecline,
        Func<Guid, CancellationToken, Task<BaseCommandResponse<Guid>>> operation, CancellationToken cancellationToken) =>
        ExecuteAsync(null, null, memberId, null, null, recipientDecline, AuthorizationActions.Delete,
            (actor, _, token) => operation(actor, token), cancellationToken);

    private async Task<BaseCommandResponse<Guid>> ExecuteAsync(Guid? requestedTenant, Guid? organizationId,
        Guid memberId, string? email, int? roleId, bool recipientDecline, string action,
        Func<Guid, Guid, CancellationToken, Task<BaseCommandResponse<Guid>>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var tenantId = context.TenantFilterTenantId ?? Guid.Empty;
        RequireContext();
        if (requestedTenant is { } requested && requested != tenantId
            || context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges()) Deny();
        if (memberId == Guid.Empty)
        {
            if (recipientDecline) return MissingInvitation();
            Deny();
        }
        Attempt? attempted = null;
        var admission = new NativeHumanMutationAdmission(context, finalizer);
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
                    var target = organizationId is { } organization
                        ? await context.OrganizationTenants.AsNoTracking().Where(row => row.TenantId == tenantId
                            && row.OrganizationId == organization).Select(row => new Target(row.Id, row.OrganizationId,
                                Guid.Empty)).SingleOrDefaultAsync(token)
                        : await context.OrganizationMembers.AsNoTracking().Where(row => row.Id == memberId && row.TenantId == tenantId)
                            .Select(row => new Target(row.OrganizationTenantId, row.OrganizationTenant.OrganizationId,
                                row.UserId)).SingleOrDefaultAsync(token);
                    if (target is null)
                    {
                        if (recipientDecline) return MissingInvitation();
                        Deny();
                    }
                    if (!recipientDecline)
                    {
                        var decision = await authorization.AuthorizeAsync(new AuthorizationRequest(
                            AuthorizationCapabilityCatalog.Require(ResourceKinds.OrganizationMember, action),
                            (organizationId ?? memberId).ToString("D"),
                            Facts: new OrganizationMemberAuthorizationFacts(tenantId, target!.OrganizationId,
                                organizationId is null ? memberId : null, organizationId is null ? target.HumanId : null),
                            Subject: new AuthorizationSubject(actor, false), Tenant: new AuthorizationTenant(tenantId)), token);
                        if (!decision.IsAllowed)
                        {
                            if (decision.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
                                throw new AuthorizationProviderUnavailableException(ResourceKinds.OrganizationMember, action);
                            Deny();
                        }
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
                            || !await context.OrganizationTenants.AsNoTracking().AnyAsync(row => row.Id == target!.ParticipationId
                                && row.TenantId == tenantId && row.OrganizationId == target.OrganizationId && !row.Organization.IsDeleted, inner)) Deny();
                        // Self-demotion/removal uses the permission fenced before this operation, not a new permission.
                        if (epochId is { } id && !await context.ModerationQualificationEpochs.AsNoTracking()
                            .AnyAsync(row => row.Id == id && row.Revision == expectedRevision, inner)) Deny();
                    }
                    return await privacy.ExecuteEnrollmentSerializedAsync(async privacyToken =>
                        await new EfCoreUnitOfWork(context).ExecuteRecognitionEnrollmentAsync(async inner =>
                        {
                            admission.RegisterOwning(RecheckAsync);
                            await using var installation = await RelationalNamedLock.AcquireTransactionAsync(
                                context, "moderation.authority-source.installation", inner);
                            var human = target!.HumanId;
                            if (organizationId is not null)
                            {
                                var candidates = await context.Users.AsNoTracking().Where(row => row.Pii != null && row.Pii.Email == email)
                                    .Select(row => row.Id).Take(2).ToArrayAsync(inner);
                                if (candidates.Length != 1) Deny();
                                human = candidates[0];
                            }
                            if ((organizationId is not null || roleId is not null)
                                && await privacy.IsSubjectFencedAsync(human, inner)) Deny();
                            await AcquireHumansAsync(tenantId, new[] { actor!.Value, human }.Distinct().Order().ToArray(), roleId, inner);
                            var epoch = await AcquireInstalledEpochAsync(tenantId, ResourceKinds.OrganizationMember,
                                action, target.OrganizationId, inner);
                            epochId = epoch?.Id;
                            expectedRevision = epoch?.Revision ?? 0;
                            await RelationalEntityRowFence.AcquireAsync<OrganizationTenant>(context, tenantId, row => row.Id, target.ParticipationId, inner);
                            var memberships = await context.OrganizationMembers.AsNoTracking().Where(row => row.TenantId == tenantId
                                && row.OrganizationTenantId == target.ParticipationId).OrderBy(row => row.Id).ToArrayAsync(inner);
                            foreach (var member in memberships)
                                await RelationalEntityRowFence.AcquireAsync<OrganizationMember>(context, tenantId, row => row.Id, member.Id, inner);
                            await RecheckAsync(inner);
                            var previous = await ReadAsync(inner);
                            if (organizationId is null && (previous is null || previous.OrganizationTenantId != target.ParticipationId
                                || previous.UserId != human || previous.IsDeleted))
                            {
                                if (recipientDecline) return MissingInvitation();
                                Deny();
                            }
                            if (recipientDecline && human != actor) return MissingInvitation();
                            if (!recipientDecline && !memberships.Any(row => row.UserId == actor && row.RoleId == (int)RoleEnum.OrgAdmin)) Deny();
                            if (roleId is { } requestedRole && !await context.Roles.AsNoTracking().AnyAsync(
                                row => row.Id == requestedRole && row.RoleScopeId == (int)RoleScopeEnum.Organization, inner)) Deny();
                            if (recipientDecline && previous!.RoleId == (int)RoleEnum.OrgAdmin
                                && memberships.Count(row => row.RoleId == (int)RoleEnum.OrgAdmin) <= 1) Deny();
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
        static BaseCommandResponse<Guid> MissingInvitation() =>
            BaseCommandResponse.Validation<Guid>(["Invitation not found"], "Invitation not found");

        Task<MemberState?> ReadAsync(CancellationToken token) => context.OrganizationMembers.AsNoTracking()
            .IgnoreQueryFilters(["SoftDelete"]).Where(row => row.Id == memberId && row.TenantId == tenantId)
            .Select(row => new MemberState(row.Id, row.TenantId, row.OrganizationTenantId, row.UserId, row.RoleId,
                row.OrganizationPositionId, row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy,
                row.IsDeleted, row.DeletedAt, row.DeletedBy)).SingleOrDefaultAsync(token);
        void RequireContext()
        {
            if (tenantId == Guid.Empty || !user.IsAuthenticated || machine.Current is not null
                || context.IsTenantFilterBypassed || context.TenantFilterTenantId != tenantId) Deny();
        }
        void Deny() => throw new AuthorizationException(ResourceKinds.OrganizationMember, action);
        static bool IsTransportFailure(Exception failure) => failure is DbException or TimeoutException or IOException
            || failure.InnerException is { } cause && IsTransportFailure(cause);
        static ConcurrencyConflictException Uncertain() => new(ConcurrencyConflictException.ConcurrentUpdate,
            "The organization membership operation cannot be reconciled against its exact committed state.");
    }

    private async Task AcquireHumansAsync(Guid tenantId, Guid[] humans, int? targetRole, CancellationToken token)
    {
        foreach (var id in humans) await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, id, token);
        foreach (var id in await context.UserExternalLogins.AsNoTracking().Where(row => humans.Contains(row.UserId))
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token))
            await RelationalEntityRowFence.AcquireGlobalAsync<UserExternalLogin>(context, id, token);
        var platform = await context.PlatformUserRoles.AsNoTracking().Where(row => humans.Contains(row.UserId))
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId }).ToArrayAsync(token);
        foreach (var grant in platform) await RelationalEntityRowFence.AcquireGlobalAsync<PlatformUserRole>(context, grant.Id, token);
        var memberships = await context.TenantUsers.AsNoTracking().Where(row => humans.Contains(row.UserId) && row.TenantId == tenantId)
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token);
        foreach (var id in memberships) await RelationalEntityRowFence.AcquireAsync<TenantUser>(context, tenantId, row => row.Id, id, token);
        var grants = await context.TenantUserRoleGrants.AsNoTracking().Where(row => row.TenantId == tenantId && memberships.Contains(row.TenantUserId))
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId }).ToArrayAsync(token);
        foreach (var grant in grants) await RelationalEntityRowFence.AcquireAsync<TenantUserRoleGrant>(context, tenantId, row => row.Id, grant.Id, token);
        foreach (var id in platform.Select(row => row.RoleId).Concat(grants.Select(row => row.RoleId))
            .Concat(targetRole is { } role ? [role] : Array.Empty<int>()).Append((int)RoleEnum.OrgAdmin).Distinct().Order())
            await RelationalEntityRowFence.AcquireLookupAsync<Role>(context, [id], token);
    }
    private sealed record Target(Guid ParticipationId, Guid OrganizationId, Guid HumanId);
    private sealed record MemberState(Guid Id, Guid TenantId, Guid OrganizationTenantId, Guid UserId, int RoleId,
        int? OrganizationPositionId, DateTime CreatedAt, Guid? CreatedBy, DateTime? UpdatedAt, Guid? UpdatedBy,
        bool IsDeleted, DateTime? DeletedAt, Guid? DeletedBy)
    { public override string ToString() => nameof(MemberState); }
    private sealed record Attempt(Guid Actor, BaseCommandResponse<Guid> Result, MemberState? Before, MemberState After,
        Guid? EpochId, long BeforeRevision, long AfterRevision)
    { public override string ToString() => nameof(Attempt); }
}
