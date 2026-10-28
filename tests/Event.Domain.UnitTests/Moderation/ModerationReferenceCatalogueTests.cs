using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Event.Domain.UnitTests.Moderation;

public class ModerationReferenceCatalogueTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task NonChatCatalogueContainsEveryRequiredProfileAndNoChatProfile()
    {
        int[] required = [1, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20,
            23, 24, 25, 26, 28, 29, 30, 31, 32, 33];
        await Assert.That(ModerationReferenceCatalogue.Profiles.Count).IsEqualTo(27);
        foreach (var code in required)
        {
            var profile = ModerationReferenceCatalogue.GetProfile((ModerationReferenceProfileId)code);
            await Assert.That((int)profile.Id).IsEqualTo(code);
            await Assert.That(profile.Code).IsEqualTo($"CFG{code:00}");
            await Assert.That(profile.ConsequencePlans.Count).IsEqualTo(1 + profile.Strategies.Count);
        }

        await Assert.That(ModerationReferenceCatalogue.Bundles.Count).IsEqualTo(10);
        await Assert.That(ModerationReferenceCatalogue.Strategies.Count).IsEqualTo(3);
    }

    [Test]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(21)]
    [Arguments(22)]
    [Arguments(27)]
    [Arguments(0)]
    [Arguments(34)]
    public async Task ChatAndUnknownIdentifiersNeverFallBackToAnotherProfile(int code)
    {
        await Reject<ArgumentOutOfRangeException>(() =>
            ModerationReferenceCatalogue.GetProfile((ModerationReferenceProfileId)code));
    }

    [Test]
    public async Task ProtectiveBundleRequiresAnExhaustivePurposeAndKeepsBothVariantsDistinct()
    {
        var bundle = ModerationReferenceCatalogue.GetBundle(ModerationConsequenceBundleKind.Protection);
        await Reject<ArgumentException>(() => bundle.Resolve());
        await Reject<ArgumentOutOfRangeException>(() => bundle.Resolve((ModerationProtectionPurpose)99));
        await Reject<ArgumentException>(() => ModerationReferenceCatalogue
            .GetBundle(ModerationConsequenceBundleKind.Unaffected).Resolve(ModerationProtectionPurpose.CredentialRecovery));

        var integrity = bundle.Resolve(ModerationProtectionPurpose.EntitlementIntegrity);
        var recovery = bundle.Resolve(ModerationProtectionPurpose.CredentialRecovery);
        await Assert.That(integrity.ExistingAdmission).IsEqualTo(ModerationExistingAdmissionMode.InvalidateProvedBadEntitlement);
        await Assert.That(integrity.Settlement).IsEqualTo(ModerationSettlementMode.AffectedParticipantNativeRights);
        await Assert.That(recovery.ExistingAdmission).IsEqualTo(ModerationExistingAdmissionMode.CurrentEligibility);
        await Assert.That(recovery.Settlement).IsEqualTo(ModerationSettlementMode.NoBookingChange);
        await Assert.That(recovery.Credentials).IsEqualTo(ModerationCredentialMode.VerifyRecoveryBeforeRelease);
        await Assert.That(ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG19).ProtectionPurpose)
            .IsEqualTo(ModerationProtectionPurpose.EntitlementIntegrity);
        await Assert.That(ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG20).ProtectionPurpose)
            .IsEqualTo(ModerationProtectionPurpose.CredentialRecovery);
    }

    [Test]
    [Arguments(3)]
    [Arguments(48)]
    public async Task EventSafetyHoldBindsTheEarlierEventEndWithoutRescheduleExtension(int hours)
    {
        var profile = ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG15);
        var end = Now.AddHours(hours);
        var bound = profile.GetResponse(ModerationReferenceGrade.G1).Timing.Bind(Now, end);
        var expected = hours == 3 ? Now.AddHours(3) : Now.AddHours(24);
        await Assert.That(bound.EndsAt).IsEqualTo(expected);
        await Assert.That(bound.ReviewAt).IsEqualTo(expected);
        await Assert.That(bound.EventWindowEnd).IsEqualTo(end);
        await Assert.That(bound.IsEffectiveAt(expected)).IsFalse();
        await Assert.That(bound.EndsAt).IsEqualTo(expected);
        await Reject<ArgumentException>(() => profile.GetResponse(ModerationReferenceGrade.G1).Timing.Bind(Now));
        await Reject<ArgumentException>(() => profile.GetResponse(ModerationReferenceGrade.G1).Timing.Bind(Now, Now));
    }

    [Test]
    public async Task ConfirmedEventExclusionUsesTheCommittedWindowNotDigitalGradeDuration()
    {
        var profile = ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG16);
        var end = Now.AddHours(6);
        foreach (var grade in new[] { ModerationReferenceGrade.G2, ModerationReferenceGrade.G3 })
        {
            var bound = profile.GetResponse(grade).Timing.Bind(Now, end);
            await Assert.That(bound.EndsAt).IsEqualTo(end);
            await Assert.That(bound.IsEffectiveAt(end.AddTicks(-1))).IsTrue();
            await Assert.That(bound.IsEffectiveAt(end)).IsFalse();
        }
    }

    [Test]
    public async Task DigitalExclusionCannotAcquirePhysicalEffectsByIncreasingGrade()
    {
        foreach (var id in new[] { ModerationReferenceProfileId.CFG12, ModerationReferenceProfileId.CFG13,
            ModerationReferenceProfileId.CFG14 })
        {
            var profile = ModerationReferenceCatalogue.GetProfile(id);
            await Assert.That(profile.CapabilityEnvelope.Contains(ModerationCapability.Admission)).IsFalse();
            await Assert.That(profile.CapabilityEnvelope.Contains(ModerationCapability.PrivateLogistics)).IsFalse();
            await Assert.That(profile.ConsequencePlans[0].ExistingAdmission).IsEqualTo(ModerationExistingAdmissionMode.CurrentEligibility);
            await Assert.That(profile.GetResponse(ModerationReferenceGrade.G4).RequiredApprovals).IsEqualTo(2);
            await Assert.That(profile.GetResponse(ModerationReferenceGrade.G4).Timing.Bind(Now).ReviewAt)
                .IsEqualTo(Now.AddDays(90));
        }

        var suite = ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG14);
        await Assert.That(suite.GetResponse(ModerationReferenceGrade.G3).RequiredApprovals).IsEqualTo(2);
        await Assert.That(suite.ScopeKinds.SetEquals([ModerationScopeKind.TrustDomain])).IsTrue();
        await Assert.That(suite.Prerequisites.Contains(ModerationReferencePrerequisite.EnrolledSolutionMapping)).IsTrue();
    }

    [Test]
    public async Task ReviewAndAudienceControlsCannotBeMistakenForOverlayDenials()
    {
        var review = ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG05);
        await Assert.That(review.Effects.Single().Mode).IsEqualTo(ModerationReferenceEffectMode.RequireReview);
        await Assert.That(review.Effects.Single().Execution).IsEqualTo(ModerationReferenceExecution.NativeOwner);
        await Assert.That(review.OverlayDenials.Count).IsEqualTo(0);

        var age = ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG28);
        await Assert.That(age.Basis).IsEqualTo(ModerationReferenceBasis.CurrentEligibility);
        await Assert.That(age.MinimumAudienceAge).IsEqualTo(18);
        await Assert.That(age.Recognition).IsEqualTo(ModerationReferenceRecognition.Never);
        await Assert.That(age.OverlayDenials.Count).IsEqualTo(0);
        await Assert.That(age.Prerequisites.Contains(ModerationReferencePrerequisite.AcceptedAgeAssurance)).IsTrue();
        await Assert.That(age.ConsequencePlans[1].Notifications).IsEqualTo(ModerationNotificationMode.SafeEligibilityAndCorrection);
    }

    [Test]
    public async Task WarningDefinitionAndObjectControlsNeverInventPersonalEnforcementIntervals()
    {
        foreach (var id in new[] { ModerationReferenceProfileId.CFG01, ModerationReferenceProfileId.CFG26,
            ModerationReferenceProfileId.CFG30 })
        {
            var profile = ModerationReferenceCatalogue.GetProfile(id);
            await Assert.That(profile.OverlayDenials.Count).IsEqualTo(0);
            await Assert.That(profile.Recognition).IsEqualTo(ModerationReferenceRecognition.Never);
            var bound = profile.Responses.Single().Timing.Bind(Now);
            await Assert.That(bound.EndsAt).IsNull();
            await Assert.That(bound.IsEffectiveAt(Now)).IsFalse();
        }

        var quarantine = ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG09);
        var window = quarantine.Responses.Single().Timing.Bind(Now);
        await Assert.That(window.EndsAt).IsNull();
        await Assert.That(window.ReviewAt).IsEqualTo(Now.AddHours(24));
        await Assert.That(window.IsEffectiveAt(Now.AddDays(2))).IsTrue();
        await Assert.That(quarantine.ConsequencePlans[0].Restoration).IsEqualTo(ModerationRestorationMode.AuthorizedObjectDecision);
    }

    [Test]
    public async Task ClassificationProgressionCountsFindingsAndChangesOnlyPublication()
    {
        foreach (var id in new[] { ModerationReferenceProfileId.CFG31, ModerationReferenceProfileId.CFG32,
            ModerationReferenceProfileId.CFG33 })
        {
            var profile = ModerationReferenceCatalogue.GetProfile(id);
            await Assert.That(profile.CapabilityEnvelope.SetEquals([ModerationCapability.Publish])).IsTrue();
            await Assert.That(profile.RecurrenceWindow).IsEqualTo(TimeSpan.FromDays(180));
            await Assert.That(profile.Prerequisites.Contains(ModerationReferencePrerequisite.DistinctFinalKnownPolicyFindings)).IsTrue();
            await Assert.That(profile.ConsequencePlans[0].Settlement).IsEqualTo(ModerationSettlementMode.NoBookingChange);
        }

        await Assert.That(ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG31)
            .GetResponse(ModerationReferenceGrade.G2).Timing.Bind(Now).EndsAt).IsEqualTo(Now.AddDays(7));
        await Assert.That(ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG33)
            .GetResponse(ModerationReferenceGrade.G3).Timing.Bind(Now).EndsAt).IsEqualTo(Now.AddDays(30));
    }

    [Test]
    public async Task HonestClassificationCorrectionNeedsNoMisconductAndPersistentEvasionNeedsItsOwnFinding()
    {
        var correction = ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG29);
        await Assert.That(correction.Basis).IsEqualTo(ModerationReferenceBasis.ClassificationCorrection);
        await Assert.That(correction.Prerequisites.Contains(ModerationReferencePrerequisite.CompatibleFinalFinding)).IsFalse();
        await Assert.That(correction.Prerequisites.Contains(ModerationReferencePrerequisite.DistinctFinalKnownPolicyFindings)).IsFalse();
        await Assert.That(correction.Recognition).IsEqualTo(ModerationReferenceRecognition.Never);
        var persistent = ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG33);
        await Assert.That(persistent.PersistentEvasionFindingMayReplaceOrdinal).IsTrue();
        await Assert.That(persistent.Ground).IsEqualTo(ModerationReferenceGround.FourthOrJustifiedPersistentClassificationEvasion);
        await Assert.That(persistent.Prerequisites.Contains(ModerationReferencePrerequisite.CompatibleFinalFinding)).IsTrue();
    }

    [Test]
    public async Task ProtectiveDeadlinesDoNotCreateGuiltOrUnboundedCredentialHolds()
    {
        var recovery = ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG20);
        var deceptive = ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG24);
        foreach (var profile in new[] { recovery, deceptive })
        {
            var response = profile.GetResponse(ModerationReferenceGrade.G1);
            var bound = response.Timing.Bind(Now);
            await Assert.That(bound.EndsAt).IsEqualTo(Now.AddHours(24));
            await Assert.That(bound.ReviewAt).IsEqualTo(Now.AddHours(24));
            await Assert.That(response.IndependentFollowUpRequired).IsTrue();
            await Assert.That(profile.Prerequisites.Contains(ModerationReferencePrerequisite.CompatibleFinalFinding)).IsFalse();
            await Assert.That(profile.RecognitionPrerequisites.Count).IsEqualTo(0);
            await Assert.That(profile.Recognition).IsEqualTo(ModerationReferenceRecognition.Never);
            await Assert.That(bound.IsEffectiveAt(Now.AddHours(24))).IsFalse();
        }

        await Assert.That(recovery.Responses.Single().Timing.Kind).IsEqualTo(ModerationReferenceTimingKind.RecoveryOrDayReviewBoundary);
        await Assert.That(deceptive.Responses.Single().Timing.Kind).IsEqualTo(ModerationReferenceTimingKind.ProtectiveDayWithReview);
    }

    [Test]
    public async Task CatalogueSelectionRejectsOverbroadScopeAndUnsupportedResponse()
    {
        var instance = Instance();
        var tenant = AuthorityScope.Tenant(instance, Guid.CreateVersion7());
        var community = AuthorityScope.Community(tenant, Guid.CreateVersion7());
        var eventScope = AuthorityScope.Resource(community, Guid.CreateVersion7());
        var eventProfile = ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG16);
        await Assert.That(eventProfile.AcceptsScope(tenant, eventScope)).IsTrue();
        await Assert.That(eventProfile.AcceptsScope(tenant, community)).IsFalse();
        await Assert.That(eventProfile.AcceptsScope(tenant, instance.Parent!)).IsFalse();
        await Reject<ArgumentOutOfRangeException>(() => eventProfile.GetResponse(ModerationReferenceGrade.G4));
        await Reject<ArgumentOutOfRangeException>(() => ModerationReferenceCatalogue.GetBundle((ModerationConsequenceBundleKind)99));
        await Reject<ArgumentOutOfRangeException>(() => ModerationReferenceCatalogue.GetStrategy((ModerationAdditiveStrategyKind)99));
    }

    [Test]
    public async Task RevokingOneCatalogueDerivedContributionPreservesIndependentPhysicalProtection()
    {
        var instance = Instance();
        var tenant = AuthorityScope.Tenant(instance, Guid.CreateVersion7());
        var eventScope = AuthorityScope.Resource(tenant, Guid.CreateVersion7());
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance);
        var publication = Sanction(ModerationReferenceProfileId.CFG06, ModerationReferenceGrade.G2,
            subject, tenant, tenant, null);
        var physical = Sanction(ModerationReferenceProfileId.CFG16, ModerationReferenceGrade.G2,
            subject, tenant, eventScope, Now.AddHours(8));
        publication.Revoke(publication.Revision, Now.AddMinutes(1));

        var admission = EffectiveRestrictions.Evaluate(subject, eventScope, ModerationCapability.Admission,
            Now.AddMinutes(2), [publication, physical], [], ModerationAuthorityState.Known);
        var publish = EffectiveRestrictions.Evaluate(subject, tenant, ModerationCapability.Publish,
            Now.AddMinutes(2), [publication, physical], [], ModerationAuthorityState.Known);
        await Assert.That(admission.Outcome).IsEqualTo(ModerationRestrictionOutcome.Denied);
        await Assert.That(admission.ContributionIds.Single()).IsEqualTo(physical.Id);
        await Assert.That(publish.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unrestricted);
        await Assert.That(physical.EndsAt).IsEqualTo(Now.AddHours(8));
    }

    [Test]
    public async Task EveryConsequenceRetainsNativePermissionAndNeverResurrectsHistoricalRights()
    {
        foreach (var profile in ModerationReferenceCatalogue.Profiles.Values)
        {
            await Assert.That(profile.Prerequisites.Contains(ModerationReferencePrerequisite.ExplicitPolicyAdoption)).IsTrue();
            await Assert.That(profile.Prerequisites.Contains(ModerationReferencePrerequisite.CurrentNativeConformance)).IsTrue();
            await Assert.That(profile.CapabilityEnvelope.Any(EffectiveRestrictions.IsProtectedRemedy)).IsFalse();
            foreach (var consequence in profile.ConsequencePlans)
            {
                await Assert.That(consequence.Safeguards.OrdinaryPermissionRequired).IsTrue();
                await Assert.That(consequence.Safeguards.PreserveIndependentPeopleRolesAndOrigin).IsTrue();
                await Assert.That(consequence.Safeguards.RecomputeRemainingContributions).IsTrue();
                await Assert.That(consequence.Safeguards.AutomaticHistoricalRestoration).IsFalse();
                await Assert.That(consequence.Safeguards.CanRecallDisclosedCopies).IsFalse();
                await Assert.That(consequence.Safeguards.PreserveProtectedRemedies).IsTrue();
            }
        }
    }

    [Test]
    public async Task ContinuityDeadlineUsesEventStartAndEscalatesAlreadyDueWork()
    {
        await Assert.That(ModerationReferenceCatalogue.ContinuityDeadline(Now, Now.AddDays(3)))
            .IsEqualTo(Now.AddHours(24));
        await Assert.That(ModerationReferenceCatalogue.ContinuityDeadline(Now, Now.AddHours(5)))
            .IsEqualTo(Now.AddHours(3));
        await Assert.That(ModerationReferenceCatalogue.ContinuityDeadline(Now, Now.AddHours(1))).IsEqualTo(Now);
    }

    private static AuthorityScope Instance() => AuthorityScope.Instance(
        AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
        Guid.CreateVersion7());

    private static ModerationSanction Sanction(
        ModerationReferenceProfileId id, ModerationReferenceGrade grade, SubjectReference subject,
        AuthorityScope issuer, AuthorityScope target, DateTimeOffset? eventEnd)
    {
        var profile = ModerationReferenceCatalogue.GetProfile(id);
        var response = profile.GetResponse(grade);
        var timing = response.Timing.Bind(Now, eventEnd);
        var decision = ModerationDecision.Propose(Guid.CreateVersion7(), Guid.CreateVersion7(), subject,
            SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), subject.Origin), issuer, target,
            ModerationRuleReference.Create(target.Id, profile.Code, 1), profile.OverlayDenials,
            timing.StartsAt, timing.EndsAt, timing.ReviewAt, response.RequiredApprovals, Now);
        decision.RecordResponse(decision.Revision, Now);
        decision.AcceptFinding(Guid.CreateVersion7(), decision.Revision, Now);
        for (var index = 0; index < response.RequiredApprovals; index++)
        {
            decision.Approve(SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), subject.Origin),
                issuer, decision.Revision, Now);
        }

        return decision.Activate(Guid.CreateVersion7(), decision.Revision, Now);
    }

    private static async Task Reject<TException>(Action action) where TException : Exception =>
        await Assert.ThrowsAsync<TException>(() =>
        {
            action();
            return Task.CompletedTask;
        });
}
