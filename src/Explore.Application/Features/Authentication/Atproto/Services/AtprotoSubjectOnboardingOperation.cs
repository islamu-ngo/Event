using System.Security.Cryptography;
using System.Text;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Application.Features.Authentication.Atproto.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Explore.Application.Features.Authentication.Atproto.Services;

public sealed record AtprotoSubjectOnboardingResult(bool Success, string FailureCode, Guid? ActorId = null, Guid? ParticipationId = null)
{
    public static AtprotoSubjectOnboardingResult Failed(string code) => new(false, code);
    public static AtprotoSubjectOnboardingResult Succeeded(Guid actorId, Guid? participationId) => new(true, string.Empty, actorId, participationId);
}

public sealed class AtprotoSubjectOnboardingOperation(
    IUserExternalLoginRepository logins, IAtprotoIdentityRepository identities, IActorRepository actors, IActorTypeRepository actorTypes,
    ITenantRepository tenants, ITenantUserRepository tenantUsers, ITenantUserRoleGrantRepository tenantRoles, IOrganizationRepository organizations,
    IOrganizationTenantRepository organizationTenants, IOrganizationMemberRepository organizationMembers, IGroupRepository groups,
    IGroupTenantRepository groupTenants, IGroupMemberRepository groupMembers, IActorReferenceConsolidationRepository references,
    IGenericRepository<ActorMerge, Guid> merges)
{
    public async Task<AtprotoSubjectOnboardingResult> ExecuteAsync(BootstrapAtprotoSessionCommand request, AtprotoVerifiedOAuthSession verified, User user, Actor userActor, Guid tenantId, DateTime at, CancellationToken cancellationToken)
    {
        AtprotoDid did = verified.Did;
        ProviderAccountKey accountKey = PlatformIdentityPrincipalExtensions.CreateAtprotoAccountKey(did);
        if (user.IsDeleted || userActor.IsDeleted || userActor.IsSuspended || userActor.UserId != user.Id)
            return AtprotoSubjectOnboardingResult.Failed("linked_identity_incomplete");
        var login = await logins.GetByProviderAndKey(accountKey).ConfigureAwait(false);
        if (login is null
            || login.UserId != user.Id
            || login.AuthenticationProviderId != (int)AuthenticationProviderKind.Atproto
            || login.ProviderKey != accountKey.Value)
            return AtprotoSubjectOnboardingResult.Failed("account_not_linked");

        var tenantUser = await tenantUsers.GetByTenantAndUserAsync(tenantId, user.Id, cancellationToken).ConfigureAwait(false);
        if (tenantUser is not null && (tenantUser.IsDeleted || tenantUser.StatusId != (int)TenantUserStatusEnum.Active
            || tenantUser.ActorId is Guid tenantActorId && tenantActorId != userActor.Id)) return AtprotoSubjectOnboardingResult.Failed("linked_identity_incomplete");

        var identity = await identities.GetByDid(did, cancellationToken).ConfigureAwait(false);
        Actor represented;
        if (identity is null)
        {
            if (request.CanonicalActorId is not null) return AtprotoSubjectOnboardingResult.Failed("classification_conflict");
            represented = await CreateAsync(request.Classification, userActor, user.Id, verified.Handle, at).ConfigureAwait(false);
            identity = new AtprotoIdentity(did);
            identity.ActorId = represented.Id;
            identity.Actor = represented;
            identity.Handle = verified.Handle;
            identity.PdsHost = verified.PdsUri.AbsoluteUri;
            identity.IsActive = true;
            identity.LastResolvedAt = at;
            identity.LastSeenAt = at;
            identity.CreatedAt = at;
            identity.CreatedBy = user.Id;
            await identities.Create(identity).ConfigureAwait(false);
        }
        else
        {
            represented = identity.Actor;
            if (identity.IsDeleted || identity.IsSuspended || !identity.IsActive || represented.IsDeleted || represented.IsSuspended)
                return AtprotoSubjectOnboardingResult.Failed("classification_conflict");
            if (request.CanonicalActorId is not null)
            {
                var canonical = await ConsolidateAsync(request, identity, tenantId, user.Id, at, cancellationToken).ConfigureAwait(false);
                if (canonical is null) return AtprotoSubjectOnboardingResult.Failed("classification_conflict");
                represented = canonical;
            }
            else if (represented.ActorTypeId == (int)ActorTypeEnum.ExternalUnclassified && request.Classification is AtprotoSubjectClassification.Organization or AtprotoSubjectClassification.Group)
            {
                represented = await PromoteAsync(represented, request.Classification, user.Id, verified.Handle, at).ConfigureAwait(false);
            }
            else if (!Matches(represented, userActor, request.Classification)) return AtprotoSubjectOnboardingResult.Failed("classification_conflict");

            identity.ActorId = represented.Id;
            identity.Actor = represented;
            identity.RefreshVerifiedMetadata(did, verified.Handle, verified.PdsUri.AbsoluteUri, null, at);
            identity.UpdatedAt = at;
            identity.UpdatedBy = user.Id;
            await identities.Update(identity).ConfigureAwait(false);
        }

        Tenant? tenant = await tenants.GetById(tenantId).ConfigureAwait(false);
        if (tenant is null)
        {
            return AtprotoSubjectOnboardingResult.Succeeded(represented.Id, null);
        }

        tenantUser ??= await tenantUsers.Create(new TenantUser { TenantId = tenantId, Tenant = tenant, UserId = user.Id, User = user, ActorId = userActor.Id, Actor = userActor, StatusId = (int)TenantUserStatusEnum.Active, JoinedAt = at, CreatedAt = at, CreatedBy = user.Id }).ConfigureAwait(false);
        if (tenantUser.ActorId is null)
        {
            tenantUser.ActorId = userActor.Id; tenantUser.Actor = userActor; tenantUser.UpdatedAt = at; tenantUser.UpdatedBy = user.Id;
            await tenantUsers.Update(tenantUser).ConfigureAwait(false);
        }

        var participationId = await EnsureParticipationAsync(request.Classification, represented, tenantUser, user, tenantId, await tenantRoles.IsTenantAdminInCurrentTenantAsync(tenantId, user.Id, cancellationToken).ConfigureAwait(false), at, cancellationToken).ConfigureAwait(false);
        return AtprotoSubjectOnboardingResult.Succeeded(represented.Id, participationId);
    }

    private async Task<Actor?> ConsolidateAsync(BootstrapAtprotoSessionCommand request, AtprotoIdentity identity, Guid tenantId, Guid userId, DateTime at, CancellationToken ct)
    {
        if (request.Classification is not (AtprotoSubjectClassification.Organization or AtprotoSubjectClassification.Group) || request.CanonicalActorId is not Guid targetId || request.ExpectedCanonicalActorConcurrencyStamp is not Guid stamp || targetId == Guid.Empty || stamp == Guid.Empty) return null;
        var source = identity.Actor;
        var target = await actors.GetById(targetId).ConfigureAwait(false);
        var type = request.Classification == AtprotoSubjectClassification.Organization ? (int)ActorTypeEnum.Organization : (int)ActorTypeEnum.Group;
        if (!IsValidCanonicalTarget(target, type, stamp)
            || !await HasCanonicalManagementAuthorityAsync(target!, tenantId, userId, ct).ConfigureAwait(false)) return null;

        var evidenceReference = BuildEvidenceReference(identity);
        if (source.Id == target!.Id)
        {
            return await references.HasCompletedConsolidationAsync(identity.Id, target.Id, evidenceReference, ct).ConfigureAwait(false)
                ? target
                : null;
        }

        if (source.ActorTypeId != (int)ActorTypeEnum.ExternalUnclassified || source.IsDeleted || source.IsSuspended || source.ExternalActorSubject?.IsDeleted != false) return null;
        if (!await references.MoveMutableReferencesAsync(source.Id, target.Id, type, ct).ConfigureAwait(false)) return null;

        await merges.Create(ActorMerge.Create(source.Id, target.Id, ActorMergeProofKind.VerifiedDid, evidenceReference, at, userId)).ConfigureAwait(false);
        source.RetireAsMergedSource(at, userId);
        source.ExternalActorSubject!.Retire(at, userId);
        await actors.Update(source).ConfigureAwait(false);
        return target;
    }

    private async Task<bool> HasCanonicalManagementAuthorityAsync(Actor target, Guid tenantId, Guid userId, CancellationToken ct)
    {
        if (target.OrganizationId is Guid organizationId)
        {
            var participation = await organizationTenants.GetByOrganizationAndTenant(organizationId, tenantId, ct).ConfigureAwait(false);
            var member = await organizationMembers.GetByOrganizationAndUser(organizationId, userId).ConfigureAwait(false);
            return participation is { IsDeleted: false, IsSuspended: false, ApprovalStatusId: (int)ApprovalStatusEnum.Approved }
                && member is { IsDeleted: false, RoleId: (int)RoleEnum.OrgAdmin }
                && member.TenantId == tenantId
                && member.OrganizationTenantId == participation.Id;
        }

        if (target.GroupId is Guid groupId)
        {
            var participation = await groupTenants.GetByGroupAndTenant(groupId, tenantId, ct).ConfigureAwait(false);
            var member = await groupMembers.GetByGroupAndUser(groupId, userId).ConfigureAwait(false);
            return participation is { IsDeleted: false, IsSuspended: false, ApprovalStatusId: (int)ApprovalStatusEnum.Approved }
                && member is { IsDeleted: false, RoleId: (int)RoleEnum.GroupAdmin }
                && member.TenantId == tenantId
                && member.GroupTenantId == participation.Id;
        }

        return false;
    }

    private static bool IsValidCanonicalTarget(Actor? target, int type, Guid stamp) =>
        target is { IsDeleted: false, IsSuspended: false }
        && target.ActorTypeId == type
        && target.ConcurrencyStamp == stamp
        && (type == (int)ActorTypeEnum.Organization
            ? target.OrganizationId is not null && target.Organization is { IsDeleted: false }
            : target.GroupId is not null && target.Group is { IsDeleted: false });

    private static string BuildEvidenceReference(AtprotoIdentity identity)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.Did))).ToLowerInvariant();
        return $"atproto-identity:{identity.Id:D};did-sha256:{digest}";
    }

    private async Task<Actor> PromoteAsync(Actor source, AtprotoSubjectClassification classification, Guid userId, string handle, DateTime at)
    {
        if (classification == AtprotoSubjectClassification.Organization)
        {
            var organization = await organizations.Create(new Organization { Pii = new OrganizationPii { FullName = handle }, CreatedAt = at, CreatedBy = userId }).ConfigureAwait(false);
            source.PromoteToOrganization(organization, await RequireActorTypeAsync(ActorTypeEnum.Organization).ConfigureAwait(false), at, userId);
        }
        else
        {
            var group = await groups.Create(new Group { FullName = handle, CreatedAt = at, CreatedBy = userId }).ConfigureAwait(false);
            source.PromoteToGroup(group, await RequireActorTypeAsync(ActorTypeEnum.Group).ConfigureAwait(false), at, userId);
        }
        await actors.Update(source).ConfigureAwait(false);
        return source;
    }

    private async Task<Actor> CreateAsync(AtprotoSubjectClassification classification, Actor userActor, Guid userId, string handle, DateTime at)
    {
        if (classification == AtprotoSubjectClassification.Person) return userActor;
        if (classification == AtprotoSubjectClassification.Organization)
        {
            var organization = await organizations.Create(new Organization { Pii = new OrganizationPii { FullName = handle }, CreatedAt = at, CreatedBy = userId }).ConfigureAwait(false);
            return await actors.Create(new Actor { ActorTypeId = (int)ActorTypeEnum.Organization, ActorType = null!, OrganizationId = organization.Id, Organization = organization, Pii = new ActorPii { DisplayName = handle }, CreatedAt = at, CreatedBy = userId }).ConfigureAwait(false);
        }
        var group = await groups.Create(new Group { FullName = handle, CreatedAt = at, CreatedBy = userId }).ConfigureAwait(false);
        return await actors.Create(new Actor { ActorTypeId = (int)ActorTypeEnum.Group, ActorType = null!, GroupId = group.Id, Group = group, Pii = new ActorPii { DisplayName = handle }, CreatedAt = at, CreatedBy = userId }).ConfigureAwait(false);
    }

    private async Task<Guid> EnsureParticipationAsync(AtprotoSubjectClassification classification, Actor represented, TenantUser tenantUser, User user, Guid tenantId, bool isTenantAdmin, DateTime at, CancellationToken ct)
    {
        if (classification == AtprotoSubjectClassification.Person) return tenantUser.Id;
        if (classification == AtprotoSubjectClassification.Organization)
        {
            var id = represented.OrganizationId!.Value;
            var participation = await organizationTenants.GetByOrganizationAndTenant(id, tenantId, ct).ConfigureAwait(false) ?? await organizationTenants.Create(new OrganizationTenant { TenantId = tenantId, Tenant = null!, OrganizationId = id, Organization = null!, ApprovalStatusId = isTenantAdmin ? (int)ApprovalStatusEnum.Approved : (int)ApprovalStatusEnum.Pending, ApprovalStatus = null!, IsVisible = isTenantAdmin, IsOrganizerEligible = isTenantAdmin, ApprovedAt = isTenantAdmin ? at : null, ApprovedBy = isTenantAdmin ? user.Id : null, CreatedAt = at, CreatedBy = user.Id }).ConfigureAwait(false);
            if (!await organizationMembers.Exists(id, user.Id).ConfigureAwait(false)) await organizationMembers.Create(new OrganizationMember { OrganizationTenantId = participation.Id, OrganizationTenant = participation, UserId = user.Id, User = user, RoleId = (int)RoleEnum.OrgAdmin, Role = null!, TenantId = tenantId, Tenant = null!, CreatedAt = at, CreatedBy = user.Id }).ConfigureAwait(false);
            return participation.Id;
        }
        var groupId = represented.GroupId!.Value;
        var groupParticipation = await groupTenants.GetByGroupAndTenant(groupId, tenantId, ct).ConfigureAwait(false) ?? await groupTenants.Create(new GroupTenant { TenantId = tenantId, Tenant = null!, GroupId = groupId, Group = null!, ApprovalStatusId = (int)ApprovalStatusEnum.Pending, ApprovalStatus = null!, CreatedAt = at, CreatedBy = user.Id }).ConfigureAwait(false);
        if (!await groupMembers.Exists(groupId, user.Id).ConfigureAwait(false)) await groupMembers.Create(new GroupMember { GroupTenantId = groupParticipation.Id, GroupTenant = groupParticipation, UserId = user.Id, User = user, RoleId = (int)RoleEnum.GroupAdmin, Role = null!, TenantId = tenantId, Tenant = null!, CreatedAt = at, CreatedBy = user.Id }).ConfigureAwait(false);
        return groupParticipation.Id;
    }

    private async Task<ActorType> RequireActorTypeAsync(ActorTypeEnum type) => await actorTypes.GetById((int)type).ConfigureAwait(false) ?? throw new InvalidOperationException($"{type} Actor type is unavailable.");
    private static bool Matches(Actor represented, Actor userActor, AtprotoSubjectClassification classification) => classification switch { AtprotoSubjectClassification.Person => represented.Id == userActor.Id && represented.UserId == userActor.UserId, AtprotoSubjectClassification.Organization => represented.OrganizationId is not null && represented.Organization is { IsDeleted: false }, AtprotoSubjectClassification.Group => represented.GroupId is not null && represented.Group is { IsDeleted: false }, _ => false };
}
