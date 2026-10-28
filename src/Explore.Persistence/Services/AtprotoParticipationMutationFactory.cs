using System.Data;
using Explore.Application.Authentication;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Services;

public sealed class AtprotoParticipationMutationFactory(
    ExploreDbContext context, IPrivacyIdentityFenceAuthority privacy) : IAtprotoParticipationMutationFactory
{
    public async Task<IAtprotoParticipationMutation> AcquireAsync(ProviderAccountKey account, Guid userId,
        Guid externalLoginId, Guid tenantId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (account.ProviderKind != AuthenticationProviderKind.Atproto || userId == Guid.Empty
            || externalLoginId == Guid.Empty || tenantId == Guid.Empty || context.IsTenantFilterBypassed
            || context.TenantFilterTenantId != tenantId || context.Database.CurrentTransaction is not { } transaction
            || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable) throw Unavailable();
        if (await privacy.IsSubjectFencedAsync(userId, cancellationToken)) throw Unavailable();
        // This lease is transaction-owned, including the absent-host/first-adoption case.
        await using var installation = await RelationalNamedLock.AcquireTransactionAsync(
            context, "moderation.authority-source.installation", cancellationToken);
        await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, userId, cancellationToken);
        var loginIds = await context.UserExternalLogins.AsNoTracking().Where(row => row.UserId == userId)
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(cancellationToken);
        foreach (var id in loginIds)
            await RelationalEntityRowFence.AcquireGlobalAsync<UserExternalLogin>(context, id, cancellationToken);
        var grants = await context.PlatformUserRoles.AsNoTracking().Where(row => row.UserId == userId)
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(cancellationToken);
        foreach (var id in grants)
            await RelationalEntityRowFence.AcquireGlobalAsync<PlatformUserRole>(context, id, cancellationToken);
        var memberships = await context.TenantUsers.AsNoTracking().Where(row => row.TenantId == tenantId && row.UserId == userId)
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(cancellationToken);
        foreach (var id in memberships)
            await RelationalEntityRowFence.AcquireAsync<TenantUser>(context, tenantId, row => row.Id, id, cancellationToken);
        var tenantGrants = await context.TenantUserRoleGrants.AsNoTracking()
            .Where(row => row.TenantId == tenantId && memberships.Contains(row.TenantUserId))
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId }).ToArrayAsync(cancellationToken);
        foreach (var grant in tenantGrants)
            await RelationalEntityRowFence.AcquireAsync<TenantUserRoleGrant>(context, tenantId, row => row.Id, grant.Id, cancellationToken);
        var roles = tenantGrants.Select(row => row.RoleId).Concat([(int)RoleEnum.OrgAdmin, (int)RoleEnum.GroupAdmin]).Distinct().Order();
        foreach (var role in roles)
            await RelationalEntityRowFence.AcquireLookupAsync<Role>(context, [role], cancellationToken);
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (host is not null)
        {
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationHostBinding>(context, host.Id, cancellationToken);
            await RelationalEntityRowFence.AcquireGlobalAsync<ServicePrincipal>(context, host.MaintenanceServicePrincipalId, cancellationToken);
            if (!await context.ServicePrincipals.AsNoTracking().AnyAsync(
                row => row.Id == host.MaintenanceServicePrincipalId && !row.IsDeleted, cancellationToken)) throw Unavailable();
        }
        await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, tenantId, cancellationToken);
        var tenant = await context.Tenants.AsNoTracking().SingleOrDefaultAsync(row => row.Id == tenantId, cancellationToken)
            ?? throw Unavailable();
        await RelationalEntityRowFence.AcquireLookupAsync<TenantStatus>(context, [tenant.TenantStatusId], cancellationToken);
        if (!await context.Tenants.AsNoTracking().AnyAsync(
            row => row.Id == tenantId && row.TenantStatus.IsActiveState, cancellationToken)) throw Unavailable();
        ModerationQualificationEpoch? epoch = null;
        if (host is not null)
        {
            var discovered = await context.ModerationQualificationEpochs.AsNoTracking()
                .SingleOrDefaultAsync(row => row.HostBindingId == host.Id && row.TenantId == tenantId, cancellationToken)
                ?? throw Unavailable();
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEpoch>(context, discovered.Id, cancellationToken);
            epoch = await context.ModerationQualificationEpochs.SingleAsync(row => row.Id == discovered.Id, cancellationToken);
            await context.Entry(epoch).ReloadAsync(cancellationToken);
            if (epoch.Revision < 1) throw Unavailable();
        }
        var existing = await context.UserExternalLogins.AsNoTracking().SingleOrDefaultAsync(
            row => row.AuthenticationProviderId == (int)account.ProviderKind && row.ProviderKey == account.Value, cancellationToken);
        if (existing is not null && (existing.UserId != userId || existing.Id != externalLoginId)) throw Unavailable();
        var participant = new Participant(context, privacy, account, userId, externalLoginId, tenantId, transaction, epoch);
        var represented = await context.AtprotoIdentities.AsNoTracking().Where(row => row.Did == account.Value)
            .Select(row => (Guid?)row.ActorId).SingleOrDefaultAsync(cancellationToken);
        if (represented is { } actorId) await participant.FenceActorAsync(actorId, cancellationToken);
        return participant;
    }

    private static InvalidOperationException Unavailable() => new("atproto_participation_authority_changed");

    private sealed class Participant(ExploreDbContext context, IPrivacyIdentityFenceAuthority privacy,
        ProviderAccountKey account, Guid userId, Guid loginId, Guid tenantId,
        IDbContextTransaction transaction, ModerationQualificationEpoch? epoch) : IAtprotoParticipationMutation
    {
        private long _revision = epoch?.Revision ?? 0;
        private readonly HashSet<Guid> _existingMembers = [];
        private readonly HashSet<Guid> _recordedMembers = [];
        private readonly HashSet<(bool Organization, Guid Owner)> _owners = [];

        public async Task FenceActorAsync(Guid actorId, CancellationToken cancellationToken)
        {
            RequireTransaction();
            await RelationalEntityRowFence.AcquireGlobalAsync<Actor>(context, actorId, cancellationToken);
            var actor = await context.Actors.AsNoTracking().SingleOrDefaultAsync(row => row.Id == actorId, cancellationToken)
                ?? throw Unavailable();
            if (actor.IsDeleted || actor.IsSuspended) throw Unavailable();
            var identityIds = await context.AtprotoIdentities.AsNoTracking().Where(row => row.ActorId == actorId)
                .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(cancellationToken);
            foreach (var id in identityIds)
                await RelationalEntityRowFence.AcquireGlobalAsync<AtprotoIdentity>(context, id, cancellationToken);
            if (actor.OrganizationId is { } organizationId && _owners.Add((true, organizationId)))
            {
                await RelationalEntityRowFence.AcquireGlobalAsync<Organization>(context, organizationId, cancellationToken);
                var participations = await context.OrganizationTenants.AsNoTracking()
                    .Where(row => row.OrganizationId == organizationId && row.TenantId == tenantId)
                    .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(cancellationToken);
                foreach (var id in participations)
                    await RelationalEntityRowFence.AcquireAsync<OrganizationTenant>(context, tenantId, row => row.Id, id, cancellationToken);
                var members = await context.OrganizationMembers.AsNoTracking()
                    .Where(row => row.TenantId == tenantId && participations.Contains(row.OrganizationTenantId))
                    .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(cancellationToken);
                foreach (var id in members)
                {
                    await RelationalEntityRowFence.AcquireAsync<OrganizationMember>(context, tenantId, row => row.Id, id, cancellationToken);
                    _existingMembers.Add(id);
                }
            }
            if (actor.GroupId is { } groupId && _owners.Add((false, groupId)))
            {
                await RelationalEntityRowFence.AcquireGlobalAsync<Group>(context, groupId, cancellationToken);
                var participations = await context.GroupTenants.AsNoTracking().Where(row => row.GroupId == groupId && row.TenantId == tenantId)
                    .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(cancellationToken);
                foreach (var id in participations)
                    await RelationalEntityRowFence.AcquireAsync<GroupTenant>(context, tenantId, row => row.Id, id, cancellationToken);
                var members = await context.GroupMembers.AsNoTracking()
                    .Where(row => row.TenantId == tenantId && participations.Contains(row.GroupTenantId))
                    .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(cancellationToken);
                foreach (var id in members)
                {
                    await RelationalEntityRowFence.AcquireAsync<GroupMember>(context, tenantId, row => row.Id, id, cancellationToken);
                    _existingMembers.Add(id);
                }
            }
        }

        public async Task RecordMembershipAsync(Guid memberId, CancellationToken cancellationToken)
        {
            await EnsureCurrentAsync(cancellationToken);
            if (!await IsCurrentMembershipAsync(memberId, cancellationToken)) throw Unavailable();
            if (_existingMembers.Contains(memberId) || !_recordedMembers.Add(memberId)) return;
            if (epoch is not null)
            {
                epoch.Advance(_revision);
                _revision = epoch.Revision;
                await context.SaveChangesAsync(cancellationToken);
            }
        }

        private async Task<bool> IsCurrentMembershipAsync(Guid memberId, CancellationToken cancellationToken)
        {
            var identity = await context.AtprotoIdentities.AsNoTracking().Include(row => row.Actor)
                .SingleOrDefaultAsync(row => row.Did == account.Value && !row.IsDeleted && row.IsActive && !row.IsSuspended
                    && !row.Actor.IsDeleted && !row.Actor.IsSuspended, cancellationToken);
            if (identity is null) return false;
            return identity.Actor.OrganizationId is { } organizationId && _owners.Contains((true, organizationId))
                && await context.OrganizationMembers.AsNoTracking().AnyAsync(row => row.Id == memberId && row.TenantId == tenantId
                    && row.UserId == userId && !row.IsDeleted && row.RoleId == (int)RoleEnum.OrgAdmin
                    && !row.OrganizationTenant.IsDeleted && !row.OrganizationTenant.IsSuspended
                    && !row.OrganizationTenant.Organization.IsDeleted && row.OrganizationTenant.OrganizationId == organizationId, cancellationToken)
                || identity.Actor.GroupId is { } groupId && _owners.Contains((false, groupId))
                && await context.GroupMembers.AsNoTracking().AnyAsync(row => row.Id == memberId && row.TenantId == tenantId
                    && row.UserId == userId && !row.IsDeleted && row.RoleId == (int)RoleEnum.GroupAdmin
                    && !row.GroupTenant.IsDeleted && !row.GroupTenant.IsSuspended
                    && !row.GroupTenant.Group.IsDeleted && row.GroupTenant.GroupId == groupId, cancellationToken);
        }

        public async Task EnsureCurrentAsync(CancellationToken cancellationToken)
        {
            RequireTransaction();
            if (await privacy.IsSubjectFencedAsync(userId, cancellationToken)
                || !await context.Users.AsNoTracking().AnyAsync(row => row.Id == userId && !row.IsDeleted, cancellationToken)
                || !await context.Tenants.AsNoTracking().AnyAsync(row => row.Id == tenantId && row.TenantStatus.IsActiveState, cancellationToken)
                || !await context.UserExternalLogins.AsNoTracking().AnyAsync(row => row.Id == loginId && row.UserId == userId
                    && row.AuthenticationProviderId == (int)account.ProviderKind && row.ProviderKey == account.Value, cancellationToken)
                || !await context.TenantUsers.AsNoTracking().AnyAsync(row => row.TenantId == tenantId && row.UserId == userId
                    && !row.IsDeleted && row.StatusId == (int)TenantUserStatusEnum.Active, cancellationToken)
                || epoch is not null && !await context.ModerationQualificationEpochs.AsNoTracking()
                    .AnyAsync(row => row.Id == epoch.Id && row.Revision == _revision, cancellationToken)) throw Unavailable();
            foreach (var id in _recordedMembers)
                if (!await IsCurrentMembershipAsync(id, cancellationToken)) throw Unavailable();
        }

        private void RequireTransaction()
        {
            if (context.Database.CurrentTransaction != transaction || context.IsTenantFilterBypassed
                || context.TenantFilterTenantId != tenantId) throw Unavailable();
        }
    }
}
