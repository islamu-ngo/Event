using System.Data.Common;
using System.IO;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
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

/// <summary>Commits participating tenant-role changes and qualification invalidation as one native unit.</summary>
public sealed class TenantUserRoleGrantMutationBoundary(
    ExploreDbContext context, IPrivacyIdentityFenceAuthority privacy, IUserContext user,
    IAdminContext admin, IMachinePrincipalAccessor machine, IAuthorizationProvider authorization)
    : ITenantUserRoleGrantMutationBoundary
{
    public Task<BaseCommandResponse<Guid>> ExecuteCreateAsync(Guid tenantId, Guid tenantUserId, int roleId,
        Func<Guid, CancellationToken, Task<BaseCommandResponse<Guid>>> operation, CancellationToken cancellationToken) =>
        ExecuteAsync(tenantId, tenantUserId, roleId, null, AuthorizationActions.Create, operation,
            result => result.IsSuccess, result => result.Id, cancellationToken);

    public Task<bool> ExecuteRevokeAsync(Guid tenantId, Guid grantId,
        Func<Guid, CancellationToken, Task<bool>> operation, CancellationToken cancellationToken) =>
        ExecuteAsync(tenantId, null, null, grantId, AuthorizationActions.Delete, operation,
            result => result, _ => grantId, cancellationToken);

    private async Task<T> ExecuteAsync<T>(Guid tenantId, Guid? tenantUserId, int? roleId, Guid? grantId,
        string action, Func<Guid, CancellationToken, Task<T>> operation, Func<T, bool> changed, Func<T, Guid> resultId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        RequireContext();
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges()) Deny();
        CommittedAttempt<T>? attempted = null;
        try
        {
            return await EfCoreUnitOfWork.ExecuteBootstrapConflictRetryAsync(
            () => context.Database.CreateExecutionStrategy().ExecuteAsync<object?, T>(null, async (_, _, strategyToken) =>
            {
                RequireContext();
                if (await VerifyCommittedAsync(strategyToken)) return attempted!.Result;
                var actor = await admin.ResolveUserIdAsync(cancellationToken);
                if (actor is null || actor == Guid.Empty) Deny();
                var decision = await authorization.AuthorizeAsync(new AuthorizationRequest(
                    AuthorizationCapabilityCatalog.Require(ResourceKinds.TenantUserRoleGrant, action),
                    (grantId ?? tenantId).ToString("D"), Facts: new TenantScopedAuthorizationFacts(tenantId),
                    Subject: new AuthorizationSubject(actor, false), Tenant: new AuthorizationTenant(tenantId)), cancellationToken);
                if (!decision.IsAllowed)
                {
                    if (decision.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
                        throw new AuthorizationProviderUnavailableException(ResourceKinds.TenantUserRoleGrant, action);
                    Deny();
                }

                context.ChangeTracker.Clear();
                Guid? epochId = null;
                long expectedRevision = 0;
                async Task RecheckAsync(CancellationToken token)
                {
                    RequireContext();
                    if (await admin.ResolveUserIdAsync(token) != actor
                        || await privacy.IsSubjectFencedAsync(actor!.Value, token)
                        || !await context.Users.AsNoTracking().AnyAsync(row => row.Id == actor && !row.IsDeleted, token)
                        || !await context.Tenants.AsNoTracking().AnyAsync(
                            row => row.Id == tenantId && row.TenantStatus.IsActiveState, token)) Deny();
                    // The original permission is fenced; its intentional self-revocation remains valid.
                    if (epochId is { } id && !await context.ModerationQualificationEpochs.AsNoTracking()
                        .AnyAsync(row => row.Id == id && row.Revision == expectedRevision, token)) Deny();
                }

                return await privacy.ExecuteEnrollmentSerializedAsync(async token =>
                    await new EfCoreUnitOfWork(context).ExecuteRecognitionEnrollmentAsync(async inner =>
                    {
                        await using var installation = await RelationalNamedLock.AcquireTransactionAsync(
                            context, "moderation.authority-source.installation", inner);
                        var targetId = tenantUserId;
                        if (grantId is { } requestedGrant)
                            targetId = await context.TenantUserRoleGrants.AsNoTracking()
                                .Where(row => row.Id == requestedGrant && row.TenantId == tenantId)
                                .Select(row => (Guid?)row.TenantUserId).SingleOrDefaultAsync(inner);
                        var target = await context.TenantUsers.AsNoTracking().SingleOrDefaultAsync(
                           row => row.Id == targetId && row.TenantId == tenantId, inner);
                        if (tenantUserId is not null && target is null) Deny();
                        if (tenantUserId is not null && await privacy.IsSubjectFencedAsync(target!.UserId, inner)) Deny();
                        var humans = target is null ? new[] { actor!.Value }
                            : new[] { actor!.Value, target.UserId }.Distinct().Order().ToArray();
                        await AcquireHumansAsync(tenantId, humans, roleId, inner);
                        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(inner);
                        if (host is not null)
                        {
                            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationHostBinding>(context, host.Id, inner);
                            await RelationalEntityRowFence.AcquireGlobalAsync<ServicePrincipal>(context, host.MaintenanceServicePrincipalId, inner);
                            if (!await context.ServicePrincipals.AsNoTracking().AnyAsync(
                                row => row.Id == host.MaintenanceServicePrincipalId && !row.IsDeleted, inner)) Deny();
                        }
                        await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, tenantId, inner);
                        var tenant = await context.Tenants.AsNoTracking().SingleOrDefaultAsync(row => row.Id == tenantId, inner);
                        if (tenant is null) Deny();
                        await RelationalEntityRowFence.AcquireLookupAsync<TenantStatus>(context, [tenant!.TenantStatusId], inner);
                        ModerationQualificationEpoch? epoch = null;
                        if (host is not null)
                        {
                            var discovered = await context.ModerationQualificationEpochs.AsNoTracking().SingleOrDefaultAsync(
                                row => row.HostBindingId == host.Id && row.TenantId == tenantId, inner);
                            if (discovered is null) Deny();
                            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEpoch>(context, discovered!.Id, inner);
                            epoch = await context.ModerationQualificationEpochs.SingleAsync(row => row.Id == discovered.Id, inner);
                            epochId = epoch.Id;
                            expectedRevision = epoch.Revision;
                            if (expectedRevision < 1) Deny();
                        }
                        await RecheckAsync(inner);
                        if (!await admin.IsTenantAdminAsync(tenantId, inner)) Deny();
                        var previous = grantId is { } existing ? await ReadGrantAsync(existing, inner) : null;
                        var result = await operation(actor!.Value, inner);
                        if (changed(result))
                        {
                            var previousRevision = expectedRevision;
                            if (epoch is not null)
                            {
                                epoch.Advance(expectedRevision);
                                expectedRevision = epoch.Revision;
                            }
                            await context.SaveChangesAsync(inner);
                            var persisted = await ReadGrantAsync(resultId(result), inner)
                                ?? throw UncertainCommit();
                            attempted = new(actor.Value, result, previous, persisted, epochId, previousRevision, expectedRevision);
                        }
                        return result;
                    }, RecheckAsync, token), RecheckAsync, cancellationToken);
            }, async (_, _, token) => new ExecutionResult<T>(await VerifyCommittedAsync(token), attempted is null ? default! : attempted.Result),
                cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (attempted is not null && IsTransportFailure(exception))
        {
            // Non-retrying providers never invoke EF's verification callback.
            if (await VerifyCommittedAsync(cancellationToken)) return attempted!.Result;
            throw;
        }

        static bool IsTransportFailure(Exception failure) => failure is DbException or TimeoutException or IOException
            || failure.InnerException is { } cause && IsTransportFailure(cause);

        async Task<bool> VerifyCommittedAsync(CancellationToken token)
        {
            if (attempted is not { } receipt) return false;
            RequireContext();
            if (await admin.ResolveUserIdAsync(token) != receipt.Actor) Deny();
            context.ChangeTracker.Clear();
            return await privacy.ExecuteEnrollmentSerializedAsync(async privacyToken =>
                await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async inner =>
                {
                    var actual = await ReadGrantAsync(receipt.After.Id, inner);
                    var revision = receipt.EpochId is { } id
                        ? await context.ModerationQualificationEpochs.AsNoTracking().Where(row => row.Id == id)
                            .Select(row => (long?)row.Revision).SingleOrDefaultAsync(inner) : 0;
                    if (actual == receipt.After && revision == receipt.AfterRevision) return true;
                    if (actual == receipt.Before && revision == receipt.BeforeRevision)
                    {
                        attempted = null;
                        return false;
                    }
                    // A changed successor is neither proof of our commit nor permission to replay it.
                    throw UncertainCommit();
                }, privacyToken), _ => Task.CompletedTask, token);
        }

        Task<GrantState?> ReadGrantAsync(Guid id, CancellationToken token) => context.TenantUserRoleGrants.AsNoTracking()
            .Where(row => row.Id == id && row.TenantId == tenantId)
            .Select(row => new GrantState(row.Id, row.TenantId, row.TenantUserId, row.RoleId, row.RoleScopeId,
                row.GrantedAt, row.GrantedBy, row.RevokedAt, row.RevokedBy, row.RevocationReason,
                row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy)).SingleOrDefaultAsync(token);

        static ConcurrencyConflictException UncertainCommit() => new(ConcurrencyConflictException.ConcurrentUpdate,
            "The tenant-role operation cannot be reconciled against its exact committed state.");

        void RequireContext()
        {
            if (tenantId == Guid.Empty || !user.IsAuthenticated || machine.Current is not null
                || context.IsTenantFilterBypassed || context.TenantFilterTenantId != tenantId) Deny();
        }

        void Deny() => throw new AuthorizationException(ResourceKinds.TenantUserRoleGrant, action);
    }

    private async Task AcquireHumansAsync(Guid tenantId, Guid[] humans, int? targetRole, CancellationToken token)
    {
        foreach (var id in humans.Order()) await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, id, token);
        var logins = await context.UserExternalLogins.AsNoTracking().Where(row => humans.Contains(row.UserId))
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token);
        foreach (var id in logins) await RelationalEntityRowFence.AcquireGlobalAsync<UserExternalLogin>(context, id, token);
        var platform = await context.PlatformUserRoles.AsNoTracking().Where(row => humans.Contains(row.UserId))
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId }).ToArrayAsync(token);
        foreach (var grant in platform) await RelationalEntityRowFence.AcquireGlobalAsync<PlatformUserRole>(context, grant.Id, token);
        var memberships = await context.TenantUsers.AsNoTracking().Where(row => row.TenantId == tenantId && humans.Contains(row.UserId))
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token);
        foreach (var id in memberships) await RelationalEntityRowFence.AcquireAsync<TenantUser>(context, tenantId, row => row.Id, id, token);
        var grants = await context.TenantUserRoleGrants.AsNoTracking().Where(row => row.TenantId == tenantId && memberships.Contains(row.TenantUserId))
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId }).ToArrayAsync(token);
        foreach (var grant in grants) await RelationalEntityRowFence.AcquireAsync<TenantUserRoleGrant>(context, tenantId, row => row.Id, grant.Id, token);
        var roles = platform.Select(row => row.RoleId).Concat(grants.Select(row => row.RoleId))
            .Concat(targetRole is { } role ? [role] : Array.Empty<int>()).Distinct().Order();
        foreach (var roleId in roles) await RelationalEntityRowFence.AcquireLookupAsync<Role>(context, [roleId], token);
    }

    private sealed record GrantState(Guid Id, Guid TenantId, Guid TenantUserId, int RoleId, int RoleScopeId,
        DateTime GrantedAt, Guid? GrantedBy, DateTime? RevokedAt, Guid? RevokedBy, string? RevocationReason,
        DateTime CreatedAt, Guid? CreatedBy, DateTime? UpdatedAt, Guid? UpdatedBy)
    {
        public override string ToString() => nameof(GrantState);
    }

    private sealed record CommittedAttempt<T>(Guid Actor, T Result, GrantState? Before, GrantState After,
        Guid? EpochId, long BeforeRevision, long AfterRevision)
    {
        public override string ToString() => nameof(CommittedAttempt<T>);
    }
}
