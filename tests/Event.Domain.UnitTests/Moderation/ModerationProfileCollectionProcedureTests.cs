using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationProfileCollectionProcedureTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(8)]
    [Arguments(9)]
    [Arguments(10)]
    [Arguments(11)]
    [Arguments(12)]
    [Arguments(13)]
    [Arguments(14)]
    [Arguments(15)]
    [Arguments(16)]
    [Arguments(17)]
    [Arguments(18)]
    public async Task Procedure_RejectsUnsafeOrIncompleteFieldVocabulary(int change)
    {
        var gates = new[] { ModerationProfilePreliminaryFieldKind.ApplicabilityDeclaration };
        var normal = new[] { ModerationProfileNormalFieldKind.DateOfBirth };
        var purpose = ModerationIndividualPurpose.ProfileCompletion;
        var applicability = ModerationProfileCollectionApplicability.NativeIndependentlyAcceptedAssessment;
        var guardian = "Reviewed relationship reference criteria.";
        var manual = "Reviewed local manual issuer criteria.";
        switch (change)
        {
            case 0: gates = []; break;
            case 1: gates = [default]; break;
            case 2: gates = [gates[0], gates[0]]; break;
            case 3: gates = [(ModerationProfilePreliminaryFieldKind)ModerationProfileNormalFieldKind.DateOfBirth]; break;
            case 4: normal = []; break;
            case 5: normal = [default]; break;
            case 6: normal = [normal[0], normal[0]]; break;
            case 7: normal = [ModerationProfileNormalFieldKind.Email]; break;
            case 8: purpose = ModerationIndividualPurpose.IdentityEnrollment; break;
            case 9: applicability = default; break;
            case 10: guardian = ""; break;
            case 11: manual = new string('x', 2049); break;
            case 12: guardian = new string('x', 2049); break;
            case 13: manual = ""; break;
            case 14: purpose = (ModerationIndividualPurpose)99; break;
            case 15: applicability = (ModerationProfileCollectionApplicability)99; break;
            case 16: normal = [(ModerationProfileNormalFieldKind)ModerationProfilePreliminaryFieldKind.SubjectConsentReference]; break;
            case 17: gates = Enumerable.Repeat(gates[0], 5).ToArray(); break;
            case 18: normal = Enumerable.Repeat(normal[0], 5).ToArray(); break;
        }
        await Assert.That(() => new ModerationProfileCollectionProcedure(purpose, applicability, gates, normal,
            guardian, manual)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Procedure_SeparatesGuardianRelationshipFromGuardianConsentReference(bool complete)
    {
        ModerationProfilePreliminaryFieldKind[] fields = complete
            ? [ModerationProfilePreliminaryFieldKind.ApplicabilityDeclaration,
                ModerationProfilePreliminaryFieldKind.AccountEligibilityReference,
                ModerationProfilePreliminaryFieldKind.SubjectConsentReference,
                ModerationProfilePreliminaryFieldKind.GuardianRelationshipReference,
                ModerationProfilePreliminaryFieldKind.GuardianConsentReference]
            : [ModerationProfilePreliminaryFieldKind.ApplicabilityDeclaration,
                ModerationProfilePreliminaryFieldKind.GuardianRelationshipReference];
        var procedure = new ModerationProfileCollectionProcedure(ModerationIndividualPurpose.ProfileCompletion,
            ModerationProfileCollectionApplicability.NativeIndependentlyAcceptedAssessment, fields,
            [ModerationProfileNormalFieldKind.DateOfBirth], "Reviewed relationship criteria.", "Reviewed manual issuer criteria.");
        await Assert.That(procedure.PreliminaryFields.Count).IsEqualTo(complete ? 5 : 2);
        await Assert.That(procedure.AllowsPreliminaryField(ModerationProfilePreliminaryFieldKind.GuardianRelationshipReference)).IsTrue();
        await Assert.That(procedure.AllowsPreliminaryField(ModerationProfilePreliminaryFieldKind.GuardianConsentReference))
            .IsEqualTo(complete);
    }

    [Test]
    public async Task Procedure_DoesNotSilentlyTruncateAnAdditionalPreliminaryField()
    {
        ModerationProfilePreliminaryFieldKind[] fields =
        [
            ModerationProfilePreliminaryFieldKind.ApplicabilityDeclaration,
            ModerationProfilePreliminaryFieldKind.AccountEligibilityReference,
            ModerationProfilePreliminaryFieldKind.SubjectConsentReference,
            ModerationProfilePreliminaryFieldKind.GuardianRelationshipReference,
            ModerationProfilePreliminaryFieldKind.GuardianConsentReference,
            ModerationProfilePreliminaryFieldKind.ApplicabilityDeclaration
        ];
        await Assert.That(() => new ModerationProfileCollectionProcedure(ModerationIndividualPurpose.ProfileCompletion,
            ModerationProfileCollectionApplicability.NativeIndependentlyAcceptedAssessment, fields,
            [ModerationProfileNormalFieldKind.DateOfBirth], "Reviewed relationship criteria.", "Reviewed manual issuer criteria."))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Procedure_SnapshotsCallerCollections_WithNoOptionalPiiDefaults()
    {
        var gates = new List<ModerationProfilePreliminaryFieldKind>
            { ModerationProfilePreliminaryFieldKind.ApplicabilityDeclaration };
        var normal = new List<ModerationProfileNormalFieldKind> { ModerationProfileNormalFieldKind.DateOfBirth };
        var procedure = new ModerationProfileCollectionProcedure(ModerationIndividualPurpose.ProfileCompletion,
            ModerationProfileCollectionApplicability.NativeIndependentlyAcceptedAssessment, gates, normal,
            "Reviewed relationship criteria.", "Reviewed manual issuer criteria.");
        gates.Clear();
        normal.Add(ModerationProfileNormalFieldKind.Email);
        await Assert.That(procedure.PreliminaryFields.Count).IsEqualTo(1);
        await Assert.That(procedure.NormalFields.Count).IsEqualTo(1);
        await Assert.That(procedure.NormalFields[0]).IsEqualTo(ModerationProfileNormalFieldKind.DateOfBirth);
        await Assert.That(procedure.AllowsNormalField(ModerationProfileNormalFieldKind.Email)).IsFalse();
        await Assert.That(procedure.AllowsPreliminaryField((ModerationProfilePreliminaryFieldKind)101)).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(8)]
    [Arguments(9)]
    [Arguments(10)]
    [Arguments(11)]
    public async Task Source_RequiresExactCurrentParentAndOwnerWithinFiniteWindow(int change)
    {
        var age = Parent();
        var adoption = ParentAdoption(age);
        var writer = 6L;
        var id = Guid.CreateVersion7();
        var human = age.CollectionOwnerHumanId;
        var now = Now;
        var until = Now.AddMinutes(3);
        switch (change)
        {
            case 0: adoption = ParentAdoption(Parent()); break;
            case 1: adoption.Withdraw(1, age.ReviewerHumanId, Now); break;
            case 2: now = adoption.ValidUntil; until = now.AddMinutes(1); break;
            case 3: until = adoption.ValidUntil.AddTicks(1); break;
            case 4: human = age.RecordedByHumanId; break;
            case 5: writer = adoption.WriterGeneration; break;
            case 6: id = age.Id; break;
            case 7: id = Guid.NewGuid(); break;
            case 8: writer = long.MaxValue; break;
            case 9: until = now; break;
            case 10: now = Now.AddTicks(-1); break;
            case 11: id = age.ProfileId; break;
        }
        await Assert.That(() => ModerationProfileCollectionProcedureSource.Record(id, age, adoption,
            Procedure(), human, writer, now, until)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(8)]
    [Arguments(9)]
    [Arguments(10)]
    [Arguments(11)]
    public async Task Currentness_RejectsParentCoordinateAndPolicySubstitution(int change)
    {
        var age = Parent();
        var adoption = ParentAdoption(age);
        var source = Source(age, adoption);
        var replacement = Parent(age, change);
        await Assert.That(source.MatchesCurrent(replacement, adoption, Now)).IsFalse();
        await Assert.That(source.MatchesCurrent(age, adoption, source.ValidUntil)).IsFalse();
        await Assert.That(source.MatchesCurrent(age, adoption, Now)).IsTrue();
    }

    [Test]
    public async Task Currentness_UsesStructuralEqualityAndRejectsSameIdentityWithNewWriterOrProcedure()
    {
        var age = Parent();
        var parent = ParentAdoption(age);
        var source = Source(age, parent);
        var adoption = Adopt(source, age, parent);
        var clone = ModerationProfileCollectionProcedureSource.Record(source.Id, Parent(age), parent,
            Procedure(), age.CollectionOwnerHumanId, 6, Now, source.ValidUntil);
        await Assert.That(adoption.MatchesCurrent(clone, Parent(age), parent, Now)).IsTrue();
        var newer = ModerationProfileCollectionProcedureSource.Record(source.Id, age, parent,
            Procedure(), age.CollectionOwnerHumanId, 8, Now, source.ValidUntil);
        await Assert.That(adoption.MatchesCurrent(newer, age, parent, Now)).IsFalse();
        var otherFields = new ModerationProfileCollectionProcedure(ModerationIndividualPurpose.ProfileCompletion,
            ModerationProfileCollectionApplicability.NativeIndependentlyAcceptedAssessment,
            Procedure().PreliminaryFields,
            [ModerationProfileNormalFieldKind.DateOfBirth, ModerationProfileNormalFieldKind.Email],
            Procedure().GuardianRelationshipReviewGrounds, Procedure().ManualIssuerReviewGrounds);
        var substituted = ModerationProfileCollectionProcedureSource.Record(source.Id, age, parent,
            otherFields, age.CollectionOwnerHumanId, 6, Now, source.ValidUntil);
        await Assert.That(adoption.MatchesCurrent(substituted, age, parent, Now)).IsFalse();
    }

    [Test]
    public async Task Procedure_SupportsOnlyActualHumanProfileCompletionVocabulary()
    {
        var origin = AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        var policy = new ModerationAgePolicy(Guid.CreateVersion7(), 1, origin);
        var human = ModerationIndividualTarget.Human(SubjectReference.Create(
            ModerationSubjectKind.Human, Guid.CreateVersion7(), origin));
        ModerationIndividualBinding Binding(ModerationIndividualTarget target, ModerationIndividualPurpose purpose) =>
            new(target, policy, purpose, Guid.CreateVersion7(), 1, 1, Guid.CreateVersion7(), 1,
                Now, Now.AddMinutes(1));
        var procedure = Procedure();
        await Assert.That(procedure.Supports(Binding(human, ModerationIndividualPurpose.ProfileCompletion))).IsTrue();
        await Assert.That(procedure.Supports(Binding(human, ModerationIndividualPurpose.IdentityEnrollment))).IsFalse();
        var guest = ModerationIndividualTarget.RegistrationSession(origin, Guid.CreateVersion7(), Guid.CreateVersion7());
        await Assert.That(procedure.Supports(Binding(guest, ModerationIndividualPurpose.ProfileCompletion))).IsFalse();
    }

    [Test]
    public async Task Ownership_RejectsReviewerOrControllerSharing()
    {
        var age = Parent();
        await Assert.That(() => Parent(ownership: age.Ownership with
            { CollectionOwnerHumanId = age.ReviewerHumanId })).Throws<ArgumentException>();
        await Assert.That(() => Parent(ownership: age.Ownership with
            { CollectionControllerHumanId = age.ReviewerControllerHumanId })).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(8)]
    public async Task Adoption_RequiresIndependentReviewerAndOriginalSourceGeneration(int change)
    {
        var age = Parent();
        var parent = ParentAdoption(age);
        var source = Source(age, parent);
        var human = source.ReviewerHumanId;
        var generation = 7L;
        var id = Guid.CreateVersion7();
        var until = Now.AddMinutes(2);
        switch (change)
        {
            case 0: human = source.RecordedByHumanId; break;
            case 1: human = Guid.CreateVersion7(); break;
            case 2: generation = source.WriterGeneration; break;
            case 3: until = source.ValidUntil.AddTicks(1); break;
            case 4: id = source.Id; break;
            case 5: generation = long.MaxValue; break;
            case 6: until = Now; break;
            case 7: id = source.ChildAgeAdoptionId; break;
            case 8: id = Guid.NewGuid(); break;
        }
        await Assert.That(() => ModerationProfileCollectionProcedureAdoption.Adopt(id, source, age, parent,
            human, generation, Now, until)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Adoption_ClosesForWithdrawnParentOrReplacementSource()
    {
        var age = Parent();
        var parent = ParentAdoption(age);
        var source = Source(age, parent);
        var adoption = Adopt(source, age, parent);
        await Assert.That(adoption.MatchesCurrent(source, age, parent, Now)).IsTrue();
        await Assert.That(adoption.MatchesCurrent(Source(age, parent, 8), age, parent, Now)).IsFalse();
        parent.Withdraw(1, age.ReviewerHumanId, Now);
        await Assert.That(adoption.MatchesCurrent(source, age, parent, Now)).IsFalse();
        await Assert.That(() => Adopt(source, age, parent)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Withdrawal_UsesRevisionCasAndPreservesOriginalIdentityWithoutRenewal()
    {
        var age = Parent();
        var parent = ParentAdoption(age);
        var source = Source(age, parent);
        var adoption = Adopt(source, age, parent);
        var id = adoption.Id;
        var expiry = adoption.ValidUntil;
        await Assert.That(adoption.MatchesCurrent(source, age, parent, expiry)).IsFalse();
        await Assert.That(() => adoption.Withdraw(1, Guid.Empty, Now)).Throws<ArgumentException>();
        await Assert.That(() => adoption.Withdraw(1, age.ReviewerHumanId, Now.AddTicks(-1))).Throws<ArgumentException>();
        await Assert.That(() => adoption.Withdraw(2, age.ReviewerHumanId, Now)).Throws<InvalidOperationException>();
        adoption.Withdraw(1, age.ReviewerHumanId, Now.AddDays(1));
        await Assert.That(adoption.Id).IsEqualTo(id);
        await Assert.That(adoption.ValidUntil).IsEqualTo(expiry);
        await Assert.That(adoption.SourceWriterGeneration).IsEqualTo(6);
        await Assert.That(adoption.AdoptedByHumanId).IsEqualTo(age.ReviewerHumanId);
        await Assert.That(adoption.Revision).IsEqualTo(2);
        await Assert.That(adoption.MatchesCurrent(source, age, parent, Now)).IsFalse();
        await Assert.That(() => adoption.Withdraw(2, age.ReviewerHumanId, Now.AddDays(2)))
            .Throws<InvalidOperationException>();
    }

    private static ModerationProfileCollectionProcedure Procedure() => new(
        ModerationIndividualPurpose.ProfileCompletion,
        ModerationProfileCollectionApplicability.NativeIndependentlyAcceptedAssessment,
        [ModerationProfilePreliminaryFieldKind.ApplicabilityDeclaration,
            ModerationProfilePreliminaryFieldKind.AccountEligibilityReference,
            ModerationProfilePreliminaryFieldKind.SubjectConsentReference,
            ModerationProfilePreliminaryFieldKind.GuardianRelationshipReference],
        [ModerationProfileNormalFieldKind.DateOfBirth],
        "Reviewed relationship reference requirements, not relationship evidence.",
        "Reviewed local issuer requirements, not professional certification.");

    private static ModerationProfileCollectionProcedureSource Source(ModerationChildAgePolicySource age,
        ModerationChildAgePolicyAdoption adoption, long writer = 6) =>
        ModerationProfileCollectionProcedureSource.Record(Guid.CreateVersion7(), age, adoption, Procedure(),
            age.CollectionOwnerHumanId, writer, Now, Now.AddMinutes(3));

    private static ModerationChildAgePolicyAdoption ParentAdoption(ModerationChildAgePolicySource age) =>
        ModerationChildAgePolicyAdoption.Adopt(Guid.CreateVersion7(), age, age.ReviewerHumanId, 5,
            Now, Now.AddMinutes(4));

    private static ModerationProfileCollectionProcedureAdoption Adopt(ModerationProfileCollectionProcedureSource source,
        ModerationChildAgePolicySource age, ModerationChildAgePolicyAdoption parent) =>
        ModerationProfileCollectionProcedureAdoption.Adopt(Guid.CreateVersion7(), source, age, parent,
            age.ReviewerHumanId, 7, Now, Now.AddMinutes(2));

    private static ModerationChildAgePolicySource Parent(ModerationChildAgePolicySource? same = null,
        int change = -1, ModerationChildAgePolicyOwnership? ownership = null)
    {
        var reviewer = Guid.CreateVersion7();
        var owner = Guid.CreateVersion7();
        ownership ??= same?.Ownership ?? new(reviewer, reviewer, owner, owner, Guid.CreateVersion7(),
            "Distinct canonical controllers.", "Appointed privacy reviewer.",
            "Appointed child safeguarding reviewer.", "Native collection owner.");
        return ModerationChildAgePolicySource.Record(same?.Id ?? Guid.CreateVersion7(),
            change == 0 ? Guid.CreateVersion7() : same?.HostBindingId ?? Guid.CreateVersion7(),
            change == 6 ? Guid.CreateVersion7() : same?.TrustDomainId ?? Guid.CreateVersion7(),
            change == 1 ? Guid.CreateVersion7() : same?.TenantId ?? Guid.CreateVersion7(),
            change == 2 ? Guid.CreateVersion7() : same?.PolicyId ?? Guid.CreateVersion7(),
            change == 3 ? 2 : same?.PolicyRevision ?? 1,
            change == 7 ? Guid.CreateVersion7() : same?.PublicationId ?? Guid.CreateVersion7(),
            change == 8 ? Guid.CreateVersion7() : same?.DefinitionId ?? Guid.CreateVersion7(),
            change == 4 ? Guid.CreateVersion7() : same?.ProfileId ?? Guid.CreateVersion7(),
            change == 9 ? 2 : same?.ModeCode ?? 1,
            change == 10 ? new string('b', 64) : same?.ProposedStateDigest ?? new string('a', 64),
            change == 11 ? (same?.Terms ?? Terms()) with { AdulthoodAge = 21 } : same?.Terms ?? Terms(),
            ownership, same?.RecordedByHumanId ?? ownership.GovernanceControllerHumanId,
            change == 5 ? 8 : 4, Now, Now.AddMinutes(5));
    }

    private static ModerationChildAgePolicyTerms Terms() => new(
        ModerationScopeKind.Tenant, ModerationChildAgeCollection.LawfulMinimumBeforeSensitiveBodyHandling,
        ModerationChildAgeConsent.ActualSubjectPurposePolicyBoundPrerequisites,
        ModerationChildAgeAssurance.QualifiedLocalManualThresholdNotDeclaredDob,
        ModerationChildAgeProviderAssurance.NotAccepted,
        ModerationChildAgeEvidence.UnknownContradictoryExpiredRemainRestrictedNotGuilty,
        ModerationChildAgeClassification.ReviewedContentNotEventFormat, 18,
        ModerationChildAgeAudience.WholeEventUnlessCompleteSeparatelyQualifiedSegregation,
        ModerationChildAgeParticipant.ActualViewerAndParticipantNeverPurchaser,
        ModerationChildAgeCorrection.InvalidateDerivedAgeWithoutStrikeOrRecognitionRestoration,
        ModerationChildAgeProtectedAccess.CatalogueRemedyOrdinaryErasureIndependent,
        ModerationChildAgeProhibitedInference.NoChildReligionBehaviourOrUniversalIdentityFace,
        ModerationChildAgeCalendar.Gregorian, ModerationChildAgeTimeZone.Utc,
        ModerationChildAgeLeapDay.MarchFirstInNonLeapYear,
        ModerationChildAgeAdulthood.ReconsiderGuardianGrantsAndPurposes, 18,
        "Child risk review.", "Privacy purpose review.", "Minimum profile DOB collection.",
        "Native independent prerequisite assessment.", "Qualified local manual review.", null,
        "ISLAMU reviewed classification policy.", "Gregorian UTC jurisdiction review.");
}
