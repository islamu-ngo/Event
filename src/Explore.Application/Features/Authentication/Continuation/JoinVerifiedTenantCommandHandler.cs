using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Services;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Features.Authentication.Continuation;

public sealed class JoinVerifiedTenantCommandHandler(
    IHumanCommitScopePreparation humanScope,
    ITenantContext tenantContext,
    IUserExternalLoginRepository logins,
    IUserTenantNavigationRepository users,
    ITenantRepository tenants,
    ITenantUserRepository memberships,
    IActorRepository actors,
    IVisitorAccessCapabilityResolver capabilities,
    ISettingMutationLock settings,
    PrivacyIdentityFenceOperation identityFence,
    IPrivacyIdentityFenceAuthority privacy,
    IOriginalOwnerSettlementBoundary primaryOwner,
    Func<IHumanSecuritySourceGate> humanGate,
    Func<IEventDiscoveryEnrollmentGate> discoveryGate,
    Func<IHumanActionCommitFinalizer> commitFinalizer,
    TimeProvider clock) : ICommandHandler<JoinVerifiedTenantCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        JoinVerifiedTenantCommand command, CancellationToken cancellationToken = default)
    {
        if (command.TenantId == Guid.Empty || tenantContext.TenantId != command.TenantId
            || command.AccountKey.ProviderKind is not (AuthenticationProviderKind.Local or AuthenticationProviderKind.Keycloak))
            return BaseCommandResponse.Failure<Guid>("tenant_join_invalid");

        var gate = humanGate();
        await using var human = await gate.AcquireAsync(cancellationToken);
        await humanScope.PrepareAsync(cancellationToken);
        var subject = humanScope.Subject;
        if (subject is null || subject.AccountKey != command.AccountKey)
            return BaseCommandResponse.Failure<Guid>("tenant_join_unlinked");
        await using var discovery = await discoveryGate().AcquireAsync(cancellationToken);

        // Embedded privacy authority is the physical owner of its shared unit.
        // Separate authority leaves the original Runtime transaction with its owner.
        var owner = privacy as IOriginalOwnerSettlementBoundary ?? primaryOwner;
        return await settings.ExecuteOrderedGroupsAsync(
            [VisitorAccessCapabilityResolver.AuthoritySettingKeys],
            token => identityFence.ExecuteSettledEnrollmentAsync(command.AccountKey, command, owner,
                ApplyAsync, requireIdentityGate: true, token), cancellationToken);

        async Task<OriginalOwnerDecision<BaseCommandResponse<Guid>>> ApplyAsync(CancellationToken token)
        {
            gate.RequireCurrent();
            var login = await logins.GetByProviderAndKey(command.AccountKey);
            if (login is null || login.Id == Guid.Empty || login.UserId != subject.PrincipalId
                || login.AuthenticationProviderId != (int)command.AccountKey.ProviderKind
                || !string.Equals(login.ProviderKey, command.AccountKey.Value, StringComparison.Ordinal))
                return Refuse("tenant_join_unlinked");

            var user = await users.GetNavigationUserAsync(subject.PrincipalId, token);
            var tenant = await tenants.GetByIdAsNoTrackingAsync(command.TenantId, token);
            var actor = await actors.GetForIdentityEnrollmentAsync(subject.PrincipalId, token);
            if (user is null || user.IsDeleted || user.Id != subject.PrincipalId
                || tenant is null || tenant.TenantStatusId != (int)TenantStatusEnum.Active
                || actor is null || actor.IsDeleted || actor.IsSuspended
                || actor.UserId != subject.PrincipalId || actor.ActorTypeId != (int)ActorTypeEnum.User)
                return Refuse("tenant_join_unavailable");

            await identityFence.MatchAndBindAsync(command.AccountKey, subject.PrincipalId, login.Id,
                command.TenantId, replacementProvisioning: false, token);
            var existing = await memberships.GetForEnrollmentAsync(command.TenantId, subject.PrincipalId, token);
            if (existing is not null)
                return !existing.IsDeleted && existing.StatusId == (int)TenantUserStatusEnum.Active
                    && existing.ActorId == actor.Id
                    ? OriginalOwnerDecision<BaseCommandResponse<Guid>>.RollBack(BaseCommandResponse.Success(existing.Id))
                    : Refuse("tenant_join_membership_inactive");

            if (!await TenantEnrollmentAdmissionOperation.IsOpenAsync(capabilities, command.TenantId, token))
                return Refuse("tenant_join_registration_closed");

            var occurredAt = clock.GetUtcNow().UtcDateTime;
            var membership = new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = command.TenantId, Tenant = tenant,
                UserId = subject.PrincipalId, User = null!, ActorId = actor.Id, Actor = actor,
                StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = occurredAt, CreatedAt = occurredAt,
                CreatedBy = subject.PrincipalId
            };
            commitFinalizer().Register(async finalToken =>
            {
                gate.RequireCurrent();
                var currentTenant = await tenants.GetByIdAsNoTrackingAsync(command.TenantId, finalToken);
                var currentUser = await users.GetNavigationUserAsync(subject.PrincipalId, finalToken);
                var currentActor = await actors.GetForIdentityEnrollmentAsync(subject.PrincipalId, finalToken);
                var currentMembership = await memberships.GetForEnrollmentAsync(
                    command.TenantId, subject.PrincipalId, finalToken);
                if (currentTenant?.TenantStatusId != (int)TenantStatusEnum.Active
                    || currentUser is null || currentUser.IsDeleted
                    || currentActor is null || currentActor.Id != actor.Id
                    || currentActor.IsDeleted || currentActor.IsSuspended
                    || currentMembership is null || currentMembership.Id != membership.Id
                    || currentMembership.IsDeleted || currentMembership.StatusId != (int)TenantUserStatusEnum.Active
                    || currentMembership.ActorId != actor.Id)
                    throw new UnauthorizedAccessException("The original tenant enrollment sources changed.");
            }, _ => gate.RequireCurrent());
            await memberships.CreateForExistingAccountAsync(membership, token);
            return OriginalOwnerDecision<BaseCommandResponse<Guid>>.Commit(BaseCommandResponse.Success(membership.Id));
        }
    }

    private static OriginalOwnerDecision<BaseCommandResponse<Guid>> Refuse(string code) =>
        OriginalOwnerDecision<BaseCommandResponse<Guid>>.RollBack(BaseCommandResponse.Failure<Guid>(code));
}
