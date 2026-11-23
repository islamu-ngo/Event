using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Learning;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Constants;
using Explore.Domain.Learning;
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
        operation = operation with { Sources = sources };

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
        operation = operation with { Sources = sources };
        var first = LearningTourPlanResolver.Resolve(operation, sources, WireFamily.Shell);
        var again = LearningTourPlanResolver.Resolve(operation, sources, WireFamily.Shell);
        var changedSources = new LearningSourceFacts(
            tenant, new string('C', 64), sources.PolicySourceStamp, false, true, null,
            sources.EffectiveSettings);
        var changed = LearningTourPlanResolver.Resolve(operation with { Sources = changedSources },
            changedSources, WireFamily.Shell);

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
        operation = operation with { Sources = sources };

        var unavailable = LearningTourPlanResolver.Resolve(operation, sources, WireFamily.NormalAi);

        await Assert.That(unavailable.Plan.IsAvailable).IsFalse();
        await Assert.That(unavailable.Plan.Steps).IsEmpty();
        await Assert.That(unavailable.Definition).IsNull();
        await Assert.That(() => LearningTourPlanResolver.Resolve(operation with
        { TenantId = Guid.CreateVersion7() }, sources, WireFamily.Shell))
            .Throws<UnauthorizedAccessException>();
    }

    [Test]
    [Arguments(false, false, false)]
    [Arguments(true, false, true)]
    [Arguments(true, true, true)]
    public async Task GlobalPolicySeparatesAutomaticInvitationFromManualClaim(
        bool enabled, bool automaticInvitations, bool manual)
    {
        var operation = PolicyOperation(new(enabled, automaticInvitations,
            familyOverrides: [new(LearningTourFamily.Shell)]));

        var resolved = LearningTourPlanResolver.Resolve(operation, operation.Sources!, WireFamily.Shell);

        await Assert.That(resolved.Plan.IsAvailable).IsEqualTo(manual);
        await Assert.That(resolved.Plan.CanClaim).IsEqualTo(manual);
        await Assert.That(resolved.Plan.CanInvite).IsEqualTo(enabled && automaticInvitations);
        await Assert.That(resolved.Definition is not null).IsEqualTo(manual);
        await Assert.That(operation.Space).IsNull();
        await Assert.That(operation.State).IsNull();
        await Assert.That(LearningAccessPolicy.CanPractice(
            LearningGuidancePolicyCodec.Decode(operation.Sources!.GuidanceDocument), true, false)).IsEqualTo(manual);
    }

    [Test]
    [Arguments(LearningGuidanceAudience.TenantAdministrators, LearningGuidanceAudience.ActiveMembers, false, false, true, true)]
    [Arguments(LearningGuidanceAudience.ActiveMembers, LearningGuidanceAudience.TenantAdministrators, false, true, true, false)]
    [Arguments(LearningGuidanceAudience.TenantAdministrators, LearningGuidanceAudience.TenantAdministrators, true, true, true, true)]
    [Arguments(LearningGuidanceAudience.TenantAdministrators, LearningGuidanceAudience.TenantAdministrators, false, false, false, false)]
    public async Task InvitationAndManualAudiencesAreIndependent(
        LearningGuidanceAudience invitation, LearningGuidanceAudience manualAudience,
        bool administrator, bool canInvite, bool canClaim, bool canPractice)
    {
        var operation = PolicyOperation(new(invitationAudience: invitation,
            manualAccessAudience: manualAudience), administrator);

        var plan = LearningTourPlanResolver.Resolve(operation, operation.Sources!, WireFamily.Shell).Plan;

        await Assert.That(plan.CanInvite).IsEqualTo(canInvite);
        await Assert.That(plan.CanClaim).IsEqualTo(canClaim);
        await Assert.That(plan.IsAvailable).IsEqualTo(canClaim);
        await Assert.That(LearningAccessPolicy.CanPractice(
            LearningGuidancePolicyCodec.Decode(operation.Sources!.GuidanceDocument), true, administrator))
            .IsEqualTo(canPractice);
    }

    [Test]
    public async Task CurrentInvitationAudienceKeepsTourAccessAfterAdministratorEligibilityEndsButCannotAuthorizeReplay()
    {
        var operation = PolicyOperation(new(manualAccessAudience: LearningGuidanceAudience.TenantAdministrators));
        var resolved = LearningTourPlanResolver.Resolve(operation, operation.Sources!, WireFamily.Shell);
        var claim = new LearningTourClaimIntent(0, Guid.CreateVersion7(), LearningTourFamily.Shell,
            resolved.Plan.MaterialVersion, "self", null, resolved.Plan.PlanFingerprint,
            resolved.Plan.PolicySourceStamp, false);
        LearningAccessPolicy.RequireInteractiveAccess(claim, true, operation.Sources!);
        LearningAccessPolicy.RequireInteractiveAccess(claim, false, operation.Sources!);
        LearningTourStep step = resolved.Definition!.Steps.First();
        var progress = new LearningTourOutcomeIntent(1, Guid.CreateVersion7(), Guid.CreateVersion7(),
            LearningTourFamily.Shell, resolved.Plan.MaterialVersion, LearningTourAction.Progress,
            step.StepKey, step.StepVersion, null, resolved.Plan.PlanFingerprint, resolved.Plan.PolicySourceStamp,
            "self", null);
        LearningAccessPolicy.RequireInteractiveAccess(progress, true, operation.Sources!);
        LearningAccessPolicy.RequireInteractiveAccess(progress, false, operation.Sources!);
        LearningAccessPolicy.RequireInteractiveAccess(progress with
        {
            Action = LearningTourAction.Complete,
            StepKey = null,
            StepVersion = null
        }, false, operation.Sources!);
        LearningAccessPolicy.RequireInteractiveAccess(claim with { Replay = true }, true, operation.Sources!);

        await Assert.That(() => LearningAccessPolicy.RequireInteractiveAccess(
            claim with { Replay = true }, false, operation.Sources!)).Throws<AuthorizationException>();
    }

    [Test]
    [Arguments(LearningTourAction.Progress)]
    [Arguments(LearningTourAction.Complete)]
    public async Task TourAccessRejectsLostAudiencesAndChangedAcceptedPolicySources(LearningTourAction action)
    {
        var operation = PolicyOperation(new(invitationAudience: LearningGuidanceAudience.TenantAdministrators,
            manualAccessAudience: LearningGuidanceAudience.TenantAdministrators), administrator: true);
        var resolved = LearningTourPlanResolver.Resolve(operation, operation.Sources!, WireFamily.Shell);
        var claim = new LearningTourClaimIntent(0, Guid.CreateVersion7(), LearningTourFamily.Shell,
            resolved.Plan.MaterialVersion, "self", null, resolved.Plan.PlanFingerprint,
            resolved.Plan.PolicySourceStamp, false);
        LearningTourStep step = resolved.Definition!.Steps.First();
        var outcome = new LearningTourOutcomeIntent(1, Guid.CreateVersion7(), Guid.CreateVersion7(),
            LearningTourFamily.Shell, resolved.Plan.MaterialVersion, action,
            action == LearningTourAction.Progress ? step.StepKey : null,
            action == LearningTourAction.Progress ? step.StepVersion : null, null,
            resolved.Plan.PlanFingerprint, resolved.Plan.PolicySourceStamp, "self", null);

        await Assert.That(() => LearningAccessPolicy.RequireInteractiveAccess(claim, false, operation.Sources!))
            .Throws<AuthorizationException>();
        await Assert.That(() => LearningAccessPolicy.RequireInteractiveAccess(outcome, false, operation.Sources!))
            .Throws<AuthorizationException>();
        var changedSources = new LearningSourceFacts(operation.TenantId, operation.Sources!.WorkspaceSourceStamp,
            new string('C', 64), false, true, operation.Sources.GuidanceDocument, operation.Sources.EffectiveSettings);
        await Assert.That(() => LearningAccessPolicy.RequireInteractiveAccess(claim, true, changedSources))
            .Throws<ConcurrencyConflictException>();
        await Assert.That(() => LearningAccessPolicy.RequireInteractiveAccess(outcome, true, changedSources))
            .Throws<ConcurrencyConflictException>();
    }

    [Test]
    public async Task FamilyOverridesDoNotInventPracticeFamiliesOrWorkspaceAuthority()
    {
        var policy = new LearningGuidancePolicy(familyOverrides:
            [new(LearningTourFamily.Shell, enabled: false), new(LearningTourFamily.Studio)]);
        var operation = PolicyOperation(policy);

        var shell = LearningTourPlanResolver.Resolve(operation, operation.Sources!, WireFamily.Shell);
        var studio = LearningTourPlanResolver.Resolve(operation, operation.Sources!, WireFamily.Studio);

        await Assert.That(shell.Plan.CanClaim).IsFalse();
        await Assert.That(shell.Plan.CanInvite).IsFalse();
        await Assert.That(studio.Plan.IsAvailable).IsFalse();
        await Assert.That(studio.Plan.CanInvite).IsFalse();
        await Assert.That(LearningAccessPolicy.CanPractice(policy, true, false)).IsTrue();
        await Assert.That(LearningAccessPolicy.CanPractice(policy, false, true)).IsFalse();
    }

    [Test]
    public async Task NamedFamilyAudiencesOverrideInheritanceWithoutBroadeningPracticeOrOtherTours()
    {
        var policy = new LearningGuidancePolicy(
            invitationAudience: LearningGuidanceAudience.TenantAdministrators,
            manualAccessAudience: LearningGuidanceAudience.TenantAdministrators,
            familyOverrides: [new(LearningTourFamily.Shell,
                invitationAudience: LearningGuidanceAudience.ActiveMembers,
                manualAccessAudience: LearningGuidanceAudience.ActiveMembers)]);
        var operation = PolicyOperation(policy) with { NormalAiAvailable = true };

        var shell = LearningTourPlanResolver.Resolve(operation, operation.Sources!, WireFamily.Shell).Plan;
        var ai = LearningTourPlanResolver.Resolve(operation, operation.Sources!, WireFamily.NormalAi).Plan;

        await Assert.That(shell.CanInvite).IsTrue();
        await Assert.That(shell.CanClaim).IsTrue();
        await Assert.That(ai.CanInvite).IsFalse();
        await Assert.That(ai.CanClaim).IsFalse();
        await Assert.That(LearningAccessPolicy.CanPractice(policy, true, false)).IsFalse();
    }

    [Test]
    [Arguments("suppressed")]
    [Arguments("hidden")]
    [Arguments("snoozed")]
    [Arguments("skipped")]
    [Arguments("covered")]
    public async Task FrequencyEvidenceStopsInvitationsButPreservesExplicitManualEligibility(string evidence)
    {
        var operation = PolicyOperation(LearningGuidancePolicy.Default);
        var initial = LearningTourPlanResolver.Resolve(operation, operation.Sources!, WireFamily.Shell);
        var state = UserLearningState.Create(Guid.CreateVersion7(), operation.TenantId,
            operation.UserId, operation.ObservedAtUtc);
        if (evidence is "suppressed" or "hidden")
            state.UpdatePreferences(evidence == "suppressed", evidence == "hidden", false, 0, operation.ObservedAtUtc);
        else if (evidence is "snoozed" or "skipped")
            state.RecordSemanticTourAction(Guid.CreateVersion7(), initial.Definition!, null, Guid.CreateVersion7(),
                evidence == "snoozed" ? LearningTourAction.Snooze : LearningTourAction.Skip,
                null, null, evidence == "snoozed" ? LearningSnoozeDuration.OneDay : null,
                initial.Plan.PlanFingerprint, initial.Plan.PolicySourceStamp, 0, operation.ObservedAtUtc);
        else
        {
            Guid progressId = Guid.CreateVersion7(), claimId = Guid.CreateVersion7();
            state.ClaimSemanticTour(progressId, initial.Definition!, claimId, Guid.CreateVersion7(), 0,
                operation.ObservedAtUtc, initial.Plan.PlanFingerprint, initial.Plan.PolicySourceStamp);
            foreach (var step in initial.Definition!.Steps)
                state.RecordSemanticTourAction(progressId, initial.Definition, claimId, Guid.CreateVersion7(),
                    LearningTourAction.Progress, step.StepKey, step.StepVersion, null,
                    initial.Plan.PlanFingerprint, initial.Plan.PolicySourceStamp, state.Revision, operation.ObservedAtUtc);
            state.RecordSemanticTourAction(progressId, initial.Definition, claimId, Guid.CreateVersion7(),
                LearningTourAction.Complete, null, null, null, initial.Plan.PlanFingerprint,
                initial.Plan.PolicySourceStamp, state.Revision, operation.ObservedAtUtc);
        }
        operation = operation with { State = state };

        var plan = LearningTourPlanResolver.Resolve(operation, operation.Sources!, WireFamily.Shell).Plan;

        await Assert.That(plan.CanInvite).IsFalse();
        await Assert.That(plan.CanClaim).IsTrue();
        await Assert.That(plan.IsAvailable).IsTrue();
    }

    [Test]
    public async Task PlanRejectsSubstitutedDetachedSourcesEvenForTheSameTenant()
    {
        var operation = PolicyOperation(new(enabled: false));
        var substitute = new LearningSourceFacts(operation.TenantId, new string('A', 64),
            new string('B', 64), false, true, null, new Dictionary<string, ResolvedSetting>());

        await Assert.That(() => LearningTourPlanResolver.Resolve(operation, substitute, WireFamily.Shell))
            .Throws<UnauthorizedAccessException>();
    }

    [Test]
    public async Task ExistingLiveClaimBlocksInvitationAndManualClaimWithoutHidingTheTour()
    {
        var operation = PolicyOperation(LearningGuidancePolicy.Default);
        var initial = LearningTourPlanResolver.Resolve(operation, operation.Sources!, WireFamily.Shell);
        var state = UserLearningState.Create(Guid.CreateVersion7(), operation.TenantId,
            operation.UserId, operation.ObservedAtUtc);
        state.ClaimSemanticTour(Guid.CreateVersion7(), initial.Definition!, Guid.CreateVersion7(),
            Guid.CreateVersion7(), 0, operation.ObservedAtUtc, initial.Plan.PlanFingerprint, initial.Plan.PolicySourceStamp);
        operation = operation with { State = state };

        var plan = LearningTourPlanResolver.Resolve(operation, operation.Sources!, WireFamily.Shell).Plan;

        await Assert.That(plan.IsAvailable).IsTrue();
        await Assert.That(plan.CanInvite).IsFalse();
        await Assert.That(plan.CanClaim).IsFalse();
    }

    private static LearningOperationState PolicyOperation(LearningGuidancePolicy policy, bool administrator = false)
    {
        Guid tenant = Guid.CreateVersion7();
        var sources = new LearningSourceFacts(tenant, new string('A', 64), new string('B', 64),
            false, true, new(Guid.CreateVersion7(), tenant, LearningGuidancePolicyCodec.SchemaVersion,
                LearningGuidancePolicyCodec.DefaultsVersion, LearningGuidancePolicyCodec.Encode(policy),
                Guid.CreateVersion7()), new Dictionary<string, ResolvedSetting>());
        return new(tenant, Guid.CreateVersion7(), "self", null, null, null, null, [], administrator, false)
        {
            Sources = sources,
            ObservedAtUtc = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc)
        };
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
            ActorType = null!,
            Pii = null!,
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)ActorTypeEnum.User,
            UserId = facts.PrincipalId
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
            ActorType = null!,
            Pii = null!,
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)ActorTypeEnum.Organization,
            OrganizationId = organization
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
            ActorType = null!,
            Pii = null!,
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)ActorTypeEnum.Group,
            GroupId = owner
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
            ActorType = null!,
            Pii = null!,
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)ActorTypeEnum.User,
            UserId = facts.PrincipalId,
            IsSuspended = true
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
