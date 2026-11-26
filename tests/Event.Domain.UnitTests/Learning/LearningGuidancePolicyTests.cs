using Explore.Domain.Learning;

namespace Event.Domain.UnitTests.Learning;

public sealed class LearningGuidancePolicyTests
{
    [Test]
    public async Task DefaultsAllowOptionalInvitationsAndManualAccessForActiveMembers()
    {
        LearningGuidancePolicy policy = LearningGuidancePolicy.Default;

        await Assert.That(policy.Enabled).IsTrue();
        await Assert.That(policy.AutomaticInvitations).IsTrue();
        await Assert.That(policy.InvitationAudience).IsEqualTo(LearningGuidanceAudience.ActiveMembers);
        await Assert.That(policy.ManualAccessAudience).IsEqualTo(LearningGuidanceAudience.ActiveMembers);
        await Assert.That(policy.RequirementPolicy).IsEqualTo(LearningRequirementPolicy.Optional);
        await Assert.That(policy.FamilyOverrides.Count).IsEqualTo(0);
        foreach (LearningTourFamily family in Enum.GetValues<LearningTourFamily>())
        {
            var eligibility = policy.Evaluate(family, isActiveMember: true, isTenantAdministrator: false);
            await Assert.That(eligibility.CanInvite).IsTrue();
            await Assert.That(eligibility.CanAccessManually).IsTrue();
        }
    }

    [Test]
    public async Task DisabledBlocksBothPathsWhileManualOnlyPreservesExplicitAccess()
    {
        LearningGuidancePolicy disabled = new(enabled: false);
        LearningGuidancePolicy manualOnly = new(automaticInvitations: false);

        var disabledEligibility = disabled.Evaluate(LearningTourFamily.Shell, true, true);
        var manualEligibility = manualOnly.Evaluate(LearningTourFamily.Shell, true, false);

        await Assert.That(disabledEligibility.CanInvite).IsFalse();
        await Assert.That(disabledEligibility.CanAccessManually).IsFalse();
        await Assert.That(manualEligibility.CanInvite).IsFalse();
        await Assert.That(manualEligibility.CanAccessManually).IsTrue();
    }

    [Test]
    public async Task InvitationAndManualAudiencesAreEvaluatedIndependently()
    {
        LearningGuidancePolicy adminInvitations = new(
            invitationAudience: LearningGuidanceAudience.TenantAdministrators);
        LearningGuidancePolicy adminManualAccess = new(
            manualAccessAudience: LearningGuidanceAudience.TenantAdministrators);

        var invitationRestricted = adminInvitations.Evaluate(LearningTourFamily.Shell, true, false);
        var manualRestricted = adminManualAccess.Evaluate(LearningTourFamily.Shell, true, false);

        await Assert.That(invitationRestricted.CanInvite).IsFalse();
        await Assert.That(invitationRestricted.CanAccessManually).IsTrue();
        await Assert.That(manualRestricted.CanInvite).IsTrue();
        await Assert.That(manualRestricted.CanAccessManually).IsFalse();
        await Assert.That(adminInvitations.Evaluate(LearningTourFamily.Shell, true, true).CanInvite).IsTrue();
        await Assert.That(adminManualAccess.Evaluate(LearningTourFamily.Shell, true, true).CanAccessManually).IsTrue();
    }

    [Test]
    public async Task AdministratorFactDoesNotReplaceActiveMembership()
    {
        LearningGuidancePolicy policy = new(
            invitationAudience: LearningGuidanceAudience.TenantAdministrators,
            manualAccessAudience: LearningGuidanceAudience.TenantAdministrators);

        foreach (bool administrator in new[] { false, true })
        {
            var eligibility = policy.Evaluate(LearningTourFamily.Shell, false, administrator);
            await Assert.That(eligibility.CanInvite).IsFalse();
            await Assert.That(eligibility.CanAccessManually).IsFalse();
        }
    }

    [Test]
    public async Task FamilyRestrictionsDoNotAffectOtherFamilies()
    {
        LearningGuidancePolicy policy = new(familyOverrides:
        [
            new(LearningTourFamily.Shell, enabled: false),
            new(LearningTourFamily.Studio, automaticInvitations: false)
        ]);

        var shell = policy.Evaluate(LearningTourFamily.Shell, true, true);
        var studio = policy.Evaluate(LearningTourFamily.Studio, true, true);
        var events = policy.Evaluate(LearningTourFamily.EventManagement, true, false);

        await Assert.That(shell.CanInvite).IsFalse();
        await Assert.That(shell.CanAccessManually).IsFalse();
        await Assert.That(studio.CanInvite).IsFalse();
        await Assert.That(studio.CanAccessManually).IsTrue();
        await Assert.That(events.CanInvite).IsTrue();
        await Assert.That(events.CanAccessManually).IsTrue();
    }

    [Test]
    public async Task FamilyOverrideCannotReenableGlobalDisableOrAutomaticInvitations()
    {
        LearningGuidanceFamilyOverride[] overrides = [new(LearningTourFamily.Shell)];
        LearningGuidancePolicy disabled = new(enabled: false, familyOverrides: overrides);
        LearningGuidancePolicy manualOnly = new(automaticInvitations: false, familyOverrides: overrides);

        var disabledEligibility = disabled.Evaluate(LearningTourFamily.Shell, true, true);
        var manualEligibility = manualOnly.Evaluate(LearningTourFamily.Shell, true, true);

        await Assert.That(disabledEligibility.CanInvite).IsFalse();
        await Assert.That(disabledEligibility.CanAccessManually).IsFalse();
        await Assert.That(manualEligibility.CanInvite).IsFalse();
        await Assert.That(manualEligibility.CanAccessManually).IsTrue();
    }

    [Test]
    public async Task FamilyAudiencesOverrideIndependentlyAndOtherwiseInherit()
    {
        LearningGuidancePolicy policy = new(
            invitationAudience: LearningGuidanceAudience.TenantAdministrators,
            familyOverrides:
            [
                new(LearningTourFamily.Shell, invitationAudience: LearningGuidanceAudience.ActiveMembers,
                    manualAccessAudience: LearningGuidanceAudience.TenantAdministrators),
                new(LearningTourFamily.Studio)
            ]);

        var shell = policy.Evaluate(LearningTourFamily.Shell, true, false);
        var studio = policy.Evaluate(LearningTourFamily.Studio, true, false);

        await Assert.That(shell.CanInvite).IsTrue();
        await Assert.That(shell.CanAccessManually).IsFalse();
        await Assert.That(studio.CanInvite).IsFalse();
        await Assert.That(studio.CanAccessManually).IsTrue();
    }

    [Test]
    public async Task DisabledPolicyRejectsRequiredOrientationButManualOnlyAllowsIt()
    {
        await Assert.That(() => new LearningGuidancePolicy(enabled: false,
                requirementPolicy: LearningRequirementPolicy.TenantAdminOrientationAcknowledgement))
            .Throws<ArgumentException>();
        LearningGuidancePolicy policy = new(automaticInvitations: false,
            requirementPolicy: LearningRequirementPolicy.TenantAdminOrientationAcknowledgement);

        await Assert.That(policy.RequirementPolicy)
            .IsEqualTo(LearningRequirementPolicy.TenantAdminOrientationAcknowledgement);
        await Assert.That(policy.Evaluate(LearningTourFamily.Shell, true, true).CanAccessManually).IsTrue();
    }

    [Test]
    public async Task UnknownEnumsAndFamiliesAreRejected()
    {
        await Assert.That(() => new LearningGuidancePolicy(invitationAudience: (LearningGuidanceAudience)99))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new LearningGuidancePolicy(manualAccessAudience: (LearningGuidanceAudience)99))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new LearningGuidancePolicy(requirementPolicy: (LearningRequirementPolicy)99))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new LearningGuidanceFamilyOverride((LearningTourFamily)99))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new LearningGuidanceFamilyOverride(LearningTourFamily.Shell,
                invitationAudience: (LearningGuidanceAudience)99))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new LearningGuidanceFamilyOverride(LearningTourFamily.Shell,
                manualAccessAudience: (LearningGuidanceAudience)99))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => LearningGuidancePolicy.Default.Evaluate((LearningTourFamily)99, true, true))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new LearningGuidancePolicy(enabled: false).Evaluate((LearningTourFamily)99, false, false))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task OverridesRejectDuplicatesNullEntriesAndMoreThanFourItems()
    {
        await Assert.That(() => new LearningGuidancePolicy(familyOverrides:
                [new(LearningTourFamily.Shell), new(LearningTourFamily.Shell, enabled: false)]))
            .Throws<ArgumentException>();
        await Assert.That(() => new LearningGuidancePolicy(familyOverrides: [null!]))
            .Throws<ArgumentException>();
        await Assert.That(() => new LearningGuidancePolicy(familyOverrides:
            [
                new(LearningTourFamily.Shell), new(LearningTourFamily.Studio),
                new(LearningTourFamily.EventManagement), new(LearningTourFamily.NormalAi),
                new(LearningTourFamily.Shell)
            ])).Throws<ArgumentException>();

        LearningGuidancePolicy complete = new(familyOverrides:
        [
            new(LearningTourFamily.Shell), new(LearningTourFamily.Studio),
            new(LearningTourFamily.EventManagement), new(LearningTourFamily.NormalAi)
        ]);
        await Assert.That(complete.FamilyOverrides.Count).IsEqualTo(4);
    }

    [Test]
    public async Task PolicySnapshotsCallerCollectionAndDoesNotExposeMutableStorage()
    {
        List<LearningGuidanceFamilyOverride> overrides = [new(LearningTourFamily.Shell, enabled: false)];
        LearningGuidancePolicy policy = new(familyOverrides: overrides);

        overrides[0] = new(LearningTourFamily.Shell);
        overrides.Clear();

        await Assert.That(policy.FamilyOverrides.Count).IsEqualTo(1);
        await Assert.That(policy.Evaluate(LearningTourFamily.Shell, true, true).CanAccessManually).IsFalse();
        await Assert.That(() => ((IList<LearningGuidanceFamilyOverride>)policy.FamilyOverrides)
            [0] = new(LearningTourFamily.Shell)).Throws<NotSupportedException>();
    }
}
