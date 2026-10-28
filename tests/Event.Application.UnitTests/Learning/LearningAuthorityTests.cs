using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Learning;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Learning;

public sealed class LearningAuthorityTests
{
    [Test]
    public async Task OrdinaryActiveTenantMemberCanLearnWithoutAnyPublishingOrAdministratorGrant()
    {
        HumanPrincipalSourceFacts facts = Member();
        await Assert.That(LearningAccessPolicy.IsActiveLearner(facts, facts.SelectedTenantId!.Value)).IsTrue();
        await Assert.That(facts.EventCreateOrganizations).IsEmpty();
        await Assert.That(facts.PlatformGrants).IsEmpty();
        await Assert.That(facts.TenantGrants).IsEmpty();
    }

    [Test]
    public async Task MissingForeignInactiveOrAmbiguousTenantMembershipFailsClosed()
    {
        HumanPrincipalSourceFacts facts = Member();
        Guid tenant = facts.SelectedTenantId!.Value;
        await Assert.That(LearningAccessPolicy.IsActiveLearner(facts with { TenantMemberships = [] }, tenant)).IsFalse();
        await Assert.That(LearningAccessPolicy.IsActiveLearner(facts with
            { TenantMemberships = [facts.TenantMemberships[0] with { StatusId = (int)TenantUserStatusEnum.Suspended }] }, tenant)).IsFalse();
        await Assert.That(LearningAccessPolicy.IsActiveLearner(facts with
            { TenantMemberships = [facts.TenantMemberships[0] with { IsDeleted = true }] }, tenant)).IsFalse();
        await Assert.That(LearningAccessPolicy.IsActiveLearner(facts with
            { TenantMemberships = [facts.TenantMemberships[0], facts.TenantMemberships[0]] }, tenant)).IsFalse();
        await Assert.That(LearningAccessPolicy.IsActiveLearner(facts, Guid.CreateVersion7())).IsFalse();
        await Assert.That(LearningAccessPolicy.IsActiveLearner(facts with { SelectedTenantId = null }, tenant)).IsFalse();
    }

    [Test]
    public async Task DeletedAbsentOrAmbiguousInternalUserCannotLearn()
    {
        HumanPrincipalSourceFacts facts = Member();
        Guid tenant = facts.SelectedTenantId!.Value;
        await Assert.That(LearningAccessPolicy.IsActiveLearner(facts with
            { Users = [facts.Users[0] with { IsDeleted = true }] }, tenant)).IsFalse();
        await Assert.That(LearningAccessPolicy.IsActiveLearner(facts with
            { Users = [facts.Users[0], facts.Users[0]] }, tenant)).IsFalse();
        await Assert.That(LearningAccessPolicy.IsActiveLearner(facts with { Users = [] }, tenant)).IsFalse();
    }

    [Test]
    [Arguments(TenantStatusEnum.Provisioning)]
    [Arguments(TenantStatusEnum.Suspended)]
    [Arguments(TenantStatusEnum.Archived)]
    [Arguments(TenantStatusEnum.Purged)]
    public async Task TenantThatNoLongerAllowsLearningIsRejected(TenantStatusEnum status)
    {
        HumanPrincipalSourceFacts facts = Member();
        Guid tenant = facts.SelectedTenantId!.Value;
        await Assert.That(LearningAccessPolicy.IsActiveLearner(facts with
            { Tenants = [new(tenant, (int)status, false)] }, tenant)).IsFalse();
    }

    [Test]
    public async Task SelfActorIsPrivateToTheCurrentInternalLearner()
    {
        HumanPrincipalSourceFacts facts = Member();
        var actor = new Actor
        {
            ActorType = null!, Pii = null!,
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)ActorTypeEnum.User, UserId = facts.PrincipalId
        };
        await Assert.That(LearningAccessPolicy.IsActorMember(facts, actor)).IsTrue();
        actor.UserId = Guid.CreateVersion7();
        await Assert.That(LearningAccessPolicy.IsActorMember(facts, actor)).IsFalse();
    }

    [Test]
    public async Task ApprovedOrganizationMemberDoesNotNeedPublisherAuthority()
    {
        HumanPrincipalSourceFacts facts = Member();
        Guid organization = Guid.CreateVersion7();
        var actor = new Actor
        {
            ActorType = null!, Pii = null!,
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)ActorTypeEnum.Organization, OrganizationId = organization
        };
        var membership = new HumanPrincipalOwnerMembershipFact(Guid.CreateVersion7(),
            facts.SelectedTenantId.Value, Guid.CreateVersion7(), organization,
            (int)RoleEnum.OrgMember, false, false, false, (int)ApprovalStatusEnum.Approved);
        facts = facts with { Organizations = [membership] };
        await Assert.That(LearningAccessPolicy.IsActorMember(facts, actor)).IsTrue();
        await Assert.That(LearningAccessPolicy.IsActorMember(facts with
            { Organizations = [membership with { ApprovalStatusId = (int)ApprovalStatusEnum.Revoked }] }, actor)).IsFalse();
        await Assert.That(LearningAccessPolicy.IsActorMember(facts with
            { Organizations = [membership with { PlacementIsDeleted = true }] }, actor)).IsFalse();
        await Assert.That(LearningAccessPolicy.IsActorMember(facts with
            { Organizations = [membership with { PlacementIsSuspended = true }] }, actor)).IsFalse();
        await Assert.That(LearningAccessPolicy.IsActorMember(facts with
            { Organizations = [membership with { OwnerIsDeleted = true }] }, actor)).IsFalse();
        await Assert.That(LearningAccessPolicy.IsActorMember(facts with
            { Organizations = [membership with { TenantId = Guid.CreateVersion7() }] }, actor)).IsFalse();
    }

    [Test]
    public async Task GroupAndOrganizationMembershipCannotAuthorizeEachOthersActor()
    {
        HumanPrincipalSourceFacts facts = Member();
        Guid owner = Guid.CreateVersion7();
        var actor = new Actor
        {
            ActorType = null!, Pii = null!,
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)ActorTypeEnum.Group, GroupId = owner
        };
        var membership = new HumanPrincipalOwnerMembershipFact(Guid.CreateVersion7(),
            facts.SelectedTenantId.Value, Guid.CreateVersion7(), owner,
            (int)RoleEnum.GroupMember, false, false, false, (int)ApprovalStatusEnum.Approved);
        await Assert.That(LearningAccessPolicy.IsActorMember(facts with { Organizations = [membership] }, actor)).IsFalse();
        await Assert.That(LearningAccessPolicy.IsActorMember(facts with { Groups = [membership] }, actor)).IsTrue();
        await Assert.That(LearningAccessPolicy.IsActorMember(facts with
            { Groups = [membership with { PlacementIsSuspended = true }] }, actor)).IsFalse();
    }

    [Test]
    public async Task ForeignOrSuspendedActorIsNeverAUsableLearningContext()
    {
        HumanPrincipalSourceFacts facts = Member();
        var actor = new Actor
        {
            ActorType = null!, Pii = null!,
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)ActorTypeEnum.User, UserId = facts.PrincipalId, IsSuspended = true
        };
        await Assert.That(LearningAccessPolicy.IsActorMember(facts, actor)).IsFalse();
        actor.IsSuspended = false;
        await Assert.That(LearningAccessPolicy.IsActorMember(
            facts with { SelectedTenantId = Guid.CreateVersion7() }, actor)).IsFalse();
    }

    [Test]
    public async Task ScopeStampSeparatesTenantAndInternalUserWithoutBeingAnAuthorityGrant()
    {
        Guid tenant = Guid.CreateVersion7(), user = Guid.CreateVersion7();
        string stamp = LearningAccessPolicy.ScopeStamp(tenant, user);
        await Assert.That(stamp).IsNotEmpty();
        await Assert.That(LearningAccessPolicy.ScopeStamp(tenant, user)).IsEqualTo(stamp);
        await Assert.That(LearningAccessPolicy.ScopeStamp(Guid.CreateVersion7(), user)).IsNotEqualTo(stamp);
        await Assert.That(LearningAccessPolicy.ScopeStamp(tenant, Guid.CreateVersion7())).IsNotEqualTo(stamp);
    }

    private static HumanPrincipalSourceFacts Member()
    {
        Guid user = Guid.CreateVersion7(), tenant = Guid.CreateVersion7();
        return new HumanPrincipalSourceFacts(
            user, tenant, [new(user, false)], [], [], [],
            [new(Guid.CreateVersion7(), tenant, (int)TenantUserStatusEnum.Active, false)], [],
            [new(tenant, (int)TenantStatusEnum.Active, true)],
            [], [], [], false, [], [], [], [], null);
    }
}
