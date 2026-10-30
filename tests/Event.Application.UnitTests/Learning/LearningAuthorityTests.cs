using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Learning;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Constants;
using WireFamily = ISLAMU.Wire.Contracts.Learning.LearningTourFamily;

namespace Event.Application.UnitTests.Learning;

public sealed class LearningAuthorityTests
{
    [Test]
    public async Task CurrentShellPlanOmitsUnavailableStudioWithoutInventingCompletion()
    {
        Guid tenant = Guid.CreateVersion7();
        var operation = new LearningOperationState(tenant, Guid.CreateVersion7(), "self", null,
            null, null, null, [], false, false);
        var sources = new LearningSourceFacts(tenant, new string('A', 64), new string('B', 64),
            false, true, null, new Dictionary<string, ResolvedSetting>());

        var resolved = LearningTourPlanResolver.Resolve(operation, sources, WireFamily.Shell);

        await Assert.That(resolved.Plan.IsAvailable).IsTrue();
        await Assert.That(resolved.Plan.Steps.Select(step => step.StepKey))
            .IsEquivalentTo(["shell.navigation", "shell.help"]);
        await Assert.That(resolved.Definition!.Steps.Count).IsEqualTo(2);
        await Assert.That(resolved.Plan.LearnerRevision).IsEqualTo(0);
        await Assert.That(operation.State).IsNull();
    }

    [Test]
    public async Task PlanFingerprintBindsAcceptedWorkspaceSourcesWithoutCreatingAProgressNamespace()
    {
        Guid tenant = Guid.CreateVersion7();
        var operation = new LearningOperationState(tenant, Guid.CreateVersion7(), "self", null,
            null, null, null, [], false, false);
        var sources = new LearningSourceFacts(tenant, new string('A', 64), new string('B', 64),
            false, true, null, new Dictionary<string, ResolvedSetting>());
        var first = LearningTourPlanResolver.Resolve(operation, sources, WireFamily.Shell);
        var again = LearningTourPlanResolver.Resolve(operation, sources, WireFamily.Shell);
        var changed = LearningTourPlanResolver.Resolve(operation, new LearningSourceFacts(
            tenant, new string('C', 64), sources.PolicySourceStamp, false, true, null,
            sources.EffectiveSettings), WireFamily.Shell);

        await Assert.That(first.Plan.PlanFingerprint).IsEqualTo(again.Plan.PlanFingerprint);
        await Assert.That(first.Plan.PlanFingerprint).IsNotEqualTo(changed.Plan.PlanFingerprint);
        await Assert.That(first.Plan.Family).IsEqualTo(changed.Plan.Family);
        await Assert.That(first.Plan.MaterialVersion).IsEqualTo(changed.Plan.MaterialVersion);
        await Assert.That(first.Definition!.Steps).IsEquivalentTo(changed.Definition!.Steps);
    }

    [Test]
    public async Task UnavailableOrForeignSourcePlanCannotSupplyAnApplicableDefinition()
    {
        Guid tenant = Guid.CreateVersion7();
        var operation = new LearningOperationState(tenant, Guid.CreateVersion7(), "self", null,
            null, null, null, [], false, false);
        var sources = new LearningSourceFacts(tenant, new string('A', 64), new string('B', 64),
            false, true, null, new Dictionary<string, ResolvedSetting>());

        var unavailable = LearningTourPlanResolver.Resolve(operation, sources, WireFamily.NormalAi);

        await Assert.That(unavailable.Plan.IsAvailable).IsFalse();
        await Assert.That(unavailable.Plan.Steps).IsEmpty();
        await Assert.That(unavailable.Definition).IsNull();
        await Assert.That(() => LearningTourPlanResolver.Resolve(operation with
            { TenantId = Guid.CreateVersion7() }, sources, WireFamily.Shell))
            .Throws<UnauthorizedAccessException>();
    }

    [Test]
    public async Task RetainedSettingsSnapshotDoesNotAliasCallerDictionaryOrMutableGroupParser()
    {
        var settings = new Dictionary<string, ResolvedSetting>
        {
            [GovernanceSettingKeys.AiAssistant.Enabled] = new() { Value = "true" }
        };
        var sources = new LearningSourceFacts(Guid.CreateVersion7(), new string('A', 64),
            new string('B', 64), false, true, null, settings);
        settings.Clear();
        var parsed = sources.CreateAiSettings();
        parsed.Populate(new Dictionary<string, ResolvedSetting>
        {
            [GovernanceSettingKeys.AiAssistant.Enabled] = new() { Value = "false" }
        });

        await Assert.That(parsed.Enabled).IsFalse();
        await Assert.That(sources.CreateAiSettings().Enabled).IsTrue();
    }

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
