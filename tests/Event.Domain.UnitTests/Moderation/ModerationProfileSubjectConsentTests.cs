using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationProfileSubjectConsentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task SelfConsent_ProducesOnlyOriginalFiniteSubjectPrerequisite()
    {
        var data = new Data();
        var consent = data.Grant();
        var prerequisite = consent.ToPrerequisite(data.Source, data.Adoption, data.Age, data.Parent,
            data.Binding, data.Assessment, Now.AddSeconds(1));
        await Assert.That(prerequisite.Kind).IsEqualTo(ModerationCollectionPrerequisiteKind.SubjectConsent);
        await Assert.That(prerequisite.Binding).IsEqualTo(data.Binding);
        await Assert.That(prerequisite.Reference.Id).IsEqualTo(consent.Id);
        await Assert.That(prerequisite.Reference.Revision).IsEqualTo(1L);
        await Assert.That(prerequisite.Reference.Generation).IsEqualTo(1L);
        await Assert.That(prerequisite.Reference.ObservedAt).IsEqualTo(Now);
        await Assert.That(prerequisite.Reference.ValidUntil).IsEqualTo(Now.AddMinutes(1));
        await Assert.That(consent.SubjectHumanId).IsEqualTo(data.Human);
        await Assert.That(consent.Assessment.Reference).IsEqualTo(data.Assessment.Reference);
        await Assert.That(consent.Assessment.Required.Count).IsEqualTo(3);
        await Assert.That(() => ModerationCollectionReceipt.Record(Guid.CreateVersion7(), data.Binding,
            data.Assessment, [prerequisite], Now, Now.AddMinutes(1))).Throws<ArgumentException>();
        consent.ValidatePersistedState();
    }

    [Test]
    public async Task CanonicalV4HumanAndIndependentConsentGenerationArePermitted()
    {
        var data = new Data(human: Guid.NewGuid());
        var consent = data.Grant(generation: 2);
        await Assert.That(consent.SubjectHumanId).IsEqualTo(data.Human);
        await Assert.That(consent.Generation).IsEqualTo(2L);
        await Assert.That(consent.MatchesCurrentMetadata(data.Source, data.Adoption, data.Age, data.Parent,
            data.Binding, data.Assessment, Now)).IsTrue();
    }

    [Test]
    [Arguments("unrelated")]
    [Arguments("guardian")]
    [Arguments("purchaser")]
    [Arguments("empty")]
    public async Task Grant_RequiresActualSubjectRatherThanAnotherAuthenticatedHuman(string actor)
    {
        var data = new Data();
        var human = actor switch
        {
            "guardian" => data.Age.ReviewerHumanId,
            "purchaser" => data.Age.CollectionOwnerHumanId,
            "empty" => Guid.Empty,
            _ => Guid.CreateVersion7()
        };
        await Assert.That(() => data.Grant(human: human)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("subject")]
    [Arguments("guest")]
    [Arguments("purpose")]
    [Arguments("unknown-purpose")]
    [Arguments("policy")]
    [Arguments("policy-revision")]
    [Arguments("trust-jurisdiction")]
    [Arguments("foreign-tenant")]
    public async Task Grant_RejectsWrongBindingEvenWithMatchingAssessment(string change)
    {
        var data = new Data();
        if (change == "unknown-purpose")
        {
            await Assert.That(() => data.ChangeBinding(change)).Throws<ArgumentException>();
            return;
        }
        var binding = data.ChangeBinding(change);
        await Assert.That(() => data.Grant(binding: binding, assessment: data.Assess(binding)))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments("binding")]
    [Arguments("not-required")]
    [Arguments("no-reference-field")]
    public async Task Grant_RequiresExplicitAssessmentAndAllowedSubjectReference(string change)
    {
        var data = new Data(allowSubjectReference: change != "no-reference-field");
        var assessment = change switch
        {
            "binding" => data.Assess(data.ChangeBinding("stamp")),
            "not-required" => data.Assess(required: [ModerationCollectionPrerequisiteKind.GuardianConsent]),
            _ => data.Assessment
        };
        await Assert.That(() => data.Grant(assessment: assessment)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("v4-id")]
    [Arguments("assessment-id")]
    [Arguments("binding-source-id")]
    [Arguments("observation-id")]
    [Arguments("procedure-id")]
    [Arguments("adoption-id")]
    [Arguments("age-id")]
    [Arguments("parent-id")]
    [Arguments("policy-id")]
    [Arguments("zero-generation")]
    [Arguments("max-generation")]
    [Arguments("negative-generation")]
    public async Task Grant_RequiresDistinctEvidenceIdentityAndFiniteSlotGeneration(string change)
    {
        var data = new Data();
        var id = change switch
        {
            "v4-id" => Guid.NewGuid(),
            "assessment-id" => data.Assessment.Reference.Id,
            "binding-source-id" => data.Binding.SourceId,
            "observation-id" => data.Binding.ObservationStamp,
            "procedure-id" => data.Source.Id,
            "adoption-id" => data.Adoption.Id,
            "age-id" => data.Age.Id,
            "parent-id" => data.Parent.Id,
            "policy-id" => data.Binding.PolicyId,
            _ => Guid.CreateVersion7()
        };
        var generation = change switch
        {
            "zero-generation" => 0L,
            "max-generation" => long.MaxValue,
            "negative-generation" => -1L,
            _ => 1L
        };
        await Assert.That(() => data.Grant(id: id, generation: generation)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("empty-window")]
    [Arguments("infinite-end")]
    [Arguments("non-utc")]
    [Arguments("before-observation")]
    [Arguments("binding-expiry")]
    [Arguments("assessment-start")]
    [Arguments("assessment-expiry")]
    [Arguments("adoption-expiry")]
    [Arguments("withdrawn-parent")]
    [Arguments("withdrawn-adoption")]
    public async Task Grant_WindowMustFitEveryOriginalInput(string change)
    {
        var data = new Data();
        var binding = change == "binding-expiry" ? data.ChangeBinding("short-window") : data.Binding;
        var assessment = data.Assess(binding, reference: change switch
        {
            "assessment-start" => new(Guid.CreateVersion7(), 1, 1, Now.AddSeconds(1), Now.AddMinutes(1)),
            "assessment-expiry" => new(Guid.CreateVersion7(), 1, 1, Now, Now.AddSeconds(30)),
            _ => data.Assessment.Reference
        });
        if (change == "withdrawn-parent") data.Parent.Withdraw(1, data.Age.ReviewerHumanId, Now);
        if (change == "withdrawn-adoption") data.Adoption.Withdraw(1, data.Age.ReviewerHumanId, Now);
        var now = change switch
        {
            "non-utc" => Now.ToOffset(TimeSpan.FromHours(1)),
            "before-observation" => Now.AddTicks(-1),
            _ => Now
        };
        var end = change switch
        {
            "empty-window" => Now,
            "infinite-end" => DateTimeOffset.MaxValue,
            "adoption-expiry" => data.Adoption.ValidUntil.AddTicks(1),
            _ => Now.AddMinutes(1)
        };
        await Assert.That(() => data.Grant(binding: binding, assessment: assessment, now: now, end: end))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments("subject")]
    [Arguments("purpose")]
    [Arguments("source")]
    [Arguments("source-revision")]
    [Arguments("source-generation")]
    [Arguments("stamp")]
    [Arguments("sequence")]
    [Arguments("observed-at")]
    [Arguments("valid-until")]
    [Arguments("jurisdiction")]
    public async Task Currentness_RejectsNewIndividualObservationInsteadOfRebinding(string change)
    {
        var data = new Data();
        var consent = data.Grant();
        var binding = data.ChangeBinding(change);
        var assessment = data.Assess(binding, data.Assessment.Reference);
        await Assert.That(consent.MatchesCurrentMetadata(data.Source, data.Adoption, data.Age, data.Parent,
            binding, assessment, Now)).IsFalse();
        await Assert.That(() => consent.ToPrerequisite(data.Source, data.Adoption, data.Age, data.Parent,
            binding, assessment, Now)).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("id")]
    [Arguments("revision")]
    [Arguments("generation")]
    [Arguments("start")]
    [Arguments("end")]
    [Arguments("required")]
    public async Task Currentness_RejectsChangedAssessmentCoordinatesAndRequiredSet(string change)
    {
        var data = new Data();
        var consent = data.Grant();
        var original = data.Assessment.Reference;
        var reference = new ModerationIndividualNativeReference(change == "id" ? Guid.CreateVersion7() : original.Id,
            change == "revision" ? 2 : original.Revision, change == "generation" ? 2 : original.Generation,
            change == "start" ? Now.AddTicks(-1) : original.ObservedAt,
            change == "end" ? Now.AddMinutes(3) : original.ValidUntil);
        var assessment = data.Assess(reference: reference,
            required: change == "required" ? [ModerationCollectionPrerequisiteKind.SubjectConsent] : null);
        await Assert.That(consent.MatchesCurrentMetadata(data.Source, data.Adoption, data.Age, data.Parent,
            data.Binding, assessment, Now)).IsFalse();
    }

    [Test]
    [Arguments("writer")]
    [Arguments("procedure")]
    [Arguments("source-id")]
    [Arguments("adoption-id")]
    [Arguments("adoption-writer")]
    [Arguments("adoption-window")]
    [Arguments("parent-withdrawal")]
    [Arguments("adoption-withdrawal")]
    [Arguments("parent-source")]
    public async Task Currentness_RejectsReplacementsEvenWhenFreshParentsAgree(string change)
    {
        var data = new Data();
        var consent = data.Grant();
        var age = change == "parent-source" ? data.CopyAge(writer: 8) : data.Age;
        var parent = change == "parent-source"
            ? ModerationChildAgePolicyAdoption.Adopt(data.Parent.Id, age, age.ReviewerHumanId, 9, Now, Now.AddMinutes(4))
            : data.Parent;
        var source = change is "writer" or "procedure" or "source-id" or "parent-source"
            ? ModerationProfileCollectionProcedureSource.Record(change == "source-id" ? Guid.CreateVersion7() : data.Source.Id,
                age, parent, Procedure(true, change == "procedure"), age.CollectionOwnerHumanId,
                change == "parent-source" ? 10 : change == "writer" ? 8 : 6, Now, Now.AddMinutes(3))
            : data.Source;
        var adoption = ModerationProfileCollectionProcedureAdoption.Adopt(
            change == "adoption-id" ? Guid.CreateVersion7() : data.Adoption.Id, source, age, parent,
            age.ReviewerHumanId, change == "parent-source" ? 11 : change is "writer" or "adoption-writer" ? 9 : 7,
            Now, change == "adoption-window" ? Now.AddMinutes(2).AddTicks(1) : Now.AddMinutes(2));
        if (change == "parent-withdrawal") parent.Withdraw(1, age.ReviewerHumanId, Now);
        if (change == "adoption-withdrawal") adoption.Withdraw(1, age.ReviewerHumanId, Now);
        await Assert.That(consent.MatchesCurrentMetadata(source, adoption, age, parent, data.Binding,
            data.Assessment, Now)).IsFalse();
    }

    [Test]
    [Arguments("host")]
    [Arguments("trust")]
    [Arguments("tenant")]
    [Arguments("policy")]
    [Arguments("policy-revision")]
    [Arguments("publication")]
    [Arguments("definition")]
    [Arguments("profile")]
    [Arguments("mode")]
    [Arguments("digest")]
    [Arguments("terms")]
    [Arguments("age-window")]
    [Arguments("parent-id")]
    [Arguments("parent-writer")]
    [Arguments("parent-window")]
    public async Task Currentness_ComparesOriginalParentCoordinatesNotFreshAgreement(string change)
    {
        var data = new Data();
        var consent = data.Grant();
        var age = data.CopyAge(4, change);
        var parent = ModerationChildAgePolicyAdoption.Adopt(
            change == "parent-id" ? Guid.CreateVersion7() : data.Parent.Id, age, age.ReviewerHumanId,
            change == "parent-writer" ? 6 : 5, Now,
            change == "parent-window" ? Now.AddMinutes(4).AddTicks(1) : Now.AddMinutes(4));
        var source = ModerationProfileCollectionProcedureSource.Record(data.Source.Id, age, parent,
            Procedure(true), age.CollectionOwnerHumanId, change == "parent-writer" ? 7 : 6, Now, Now.AddMinutes(3));
        var adoption = ModerationProfileCollectionProcedureAdoption.Adopt(data.Adoption.Id, source, age, parent,
            age.ReviewerHumanId, change == "parent-writer" ? 8 : 7, Now, Now.AddMinutes(2));
        await Assert.That(adoption.MatchesCurrent(source, age, parent, Now)).IsTrue();
        await Assert.That(consent.MatchesCurrentMetadata(source, adoption, age, parent,
            data.Binding, data.Assessment, Now)).IsFalse();
    }

    [Test]
    public async Task ExactStructuralClonesRemainCurrentWithoutObjectIdentityAuthority()
    {
        var data = new Data();
        var consent = data.Grant();
        var age = data.CopyAge(4);
        var parent = ModerationChildAgePolicyAdoption.Adopt(data.Parent.Id, age, age.ReviewerHumanId,
            5, Now, Now.AddMinutes(4));
        var source = ModerationProfileCollectionProcedureSource.Record(data.Source.Id, age, parent,
            Procedure(true), age.CollectionOwnerHumanId, 6, Now, Now.AddMinutes(3));
        var adoption = ModerationProfileCollectionProcedureAdoption.Adopt(data.Adoption.Id, source, age, parent,
            age.ReviewerHumanId, 7, Now, Now.AddMinutes(2));
        await Assert.That(consent.MatchesCurrentMetadata(source, adoption, age, parent,
            data.Binding, data.Assess(reference: data.Assessment.Reference), Now)).IsTrue();
    }

    [Test]
    [Arguments("Id")]
    [Arguments("SubjectHumanId")]
    [Arguments("Generation")]
    [Arguments("Revision")]
    [Arguments("ProcedureAdoptionRevision")]
    [Arguments("ProcedureAdoptionWriterGeneration")]
    [Arguments("ProcedureAdoptedByHumanId")]
    [Arguments("GrantedAt")]
    [Arguments("ValidUntil")]
    [Arguments("WithdrawnAt")]
    public async Task MaterializedInvalidStateCannotBecomeUsableConsent(string property)
    {
        var data = new Data();
        var consent = data.Grant();
        object invalid = property switch
        {
            "Id" => data.Assessment.Reference.Id,
            "SubjectHumanId" => data.Age.ReviewerHumanId,
            "ProcedureAdoptedByHumanId" => Guid.Empty,
            "GrantedAt" => Now.AddTicks(-1),
            "ValidUntil" => Now.AddMinutes(3),
            "WithdrawnAt" => Now,
            _ => 0L
        };
        typeof(ModerationProfileSubjectConsent).GetProperty(property)!.SetValue(consent, invalid);
        await Assert.That(() => consent.ValidatePersistedState()).Throws<ArgumentException>();
        await Assert.That(() => consent.ToPrerequisite(data.Source, data.Adoption, data.Age, data.Parent,
            data.Binding, data.Assessment, Now)).Throws<ArgumentException>();
    }

    [Test]
    public async Task MaterializedConsent_RejectsProcedureAdoptionAliasingParentCoordinatesWithoutMutation()
    {
        var data = new Data();
        foreach (var id in new[] { data.Source.Id, data.Age.Id, data.Parent.Id,
            data.Age.HostBindingId, data.Age.TrustDomainId, data.Age.TenantId,
            data.Age.PolicyId, data.Age.PublicationId, data.Age.DefinitionId, data.Age.ProfileId })
        {
            var consent = data.Grant();
            typeof(ModerationProfileSubjectConsent)
                .GetProperty(nameof(consent.ProcedureAdoptionId))!.SetValue(consent, id);
            await Assert.That(() => consent.ValidatePersistedState()).Throws<ArgumentException>();
            await Assert.That(() => consent.Withdraw(1, data.Human, Now)).Throws<ArgumentException>();
            await Assert.That(consent.Revision).IsEqualTo(1L);
            await Assert.That(consent.WithdrawnAt).IsNull();
        }
    }

    [Test]
    public async Task RequiredKindsAndProcedureCollectionsRemainDefensiveOriginalSnapshots()
    {
        var data = new Data();
        var required = new List<ModerationCollectionPrerequisiteKind>
            { ModerationCollectionPrerequisiteKind.SubjectConsent, ModerationCollectionPrerequisiteKind.GuardianConsent };
        var assessment = data.Assess(required: required);
        var consent = data.Grant(assessment: assessment);
        required.Clear();
        await Assert.That(consent.Assessment.Required.Count).IsEqualTo(2);
        await Assert.That(consent.Source.Procedure.PreliminaryFields.Count).IsEqualTo(2);
        await Assert.That(consent.MatchesCurrentMetadata(data.Source, data.Adoption, data.Age, data.Parent,
            data.Binding, data.Assess(reference: assessment.Reference,
                required: [ModerationCollectionPrerequisiteKind.GuardianConsent, ModerationCollectionPrerequisiteKind.SubjectConsent]),
            Now)).IsTrue();
        await Assert.That(consent.ToString()).IsEqualTo(nameof(ModerationProfileSubjectConsent));
    }

    [Test]
    [Arguments(-1, false)]
    [Arguments(0, true)]
    [Arguments(59, true)]
    [Arguments(60, false)]
    public async Task OriginalConsentWindowCannotRenewDespiteLongerCurrentEvidence(int seconds, bool expected)
    {
        var data = new Data();
        var consent = data.Grant();
        await Assert.That(consent.MatchesCurrentMetadata(data.Source, data.Adoption, data.Age, data.Parent,
            data.Binding, data.Assessment, Now.AddSeconds(seconds))).IsEqualTo(expected);
        await Assert.That(consent.ValidUntil).IsEqualTo(Now.AddMinutes(1));
    }

    [Test]
    public async Task Withdrawal_IsTerminalSubjectRevisionCasEvenAfterEveryParentExpiresOrWithdraws()
    {
        var data = new Data();
        var consent = data.Grant();
        await Assert.That(() => consent.Withdraw(2, data.Human, Now)).Throws<InvalidOperationException>();
        await Assert.That(() => consent.Withdraw(1, data.Age.ReviewerHumanId, Now)).Throws<ArgumentException>();
        await Assert.That(() => consent.Withdraw(1, Guid.Empty, Now)).Throws<ArgumentException>();
        await Assert.That(() => consent.Withdraw(1, data.Human, Now.AddTicks(-1))).Throws<ArgumentException>();
        await Assert.That(() => consent.Withdraw(1, data.Human, DateTimeOffset.MaxValue)).Throws<ArgumentException>();
        await Assert.That(() => consent.Withdraw(1, data.Human, Now.ToOffset(TimeSpan.FromHours(1))))
            .Throws<ArgumentException>();
        data.Parent.Withdraw(1, data.Age.ReviewerHumanId, Now);
        data.Adoption.Withdraw(1, data.Age.ReviewerHumanId, Now);
        consent.Withdraw(1, data.Human, Now.AddDays(1));
        consent.ValidatePersistedState();
        await Assert.That(consent.Revision).IsEqualTo(2L);
        await Assert.That(consent.WithdrawnAt).IsEqualTo(Now.AddDays(1));
        await Assert.That(consent.GrantedAt).IsEqualTo(Now);
        await Assert.That(consent.ValidUntil).IsEqualTo(Now.AddMinutes(1));
        await Assert.That(consent.Generation).IsEqualTo(1L);
        await Assert.That(consent.MatchesCurrentMetadata(data.Source, data.Adoption, data.Age, data.Parent,
            data.Binding, data.Assessment, Now)).IsFalse();
        await Assert.That(() => consent.Withdraw(1, data.Human, Now.AddDays(1))).Throws<InvalidOperationException>();
        await Assert.That(() => consent.Withdraw(2, data.Human, Now.AddDays(2))).Throws<InvalidOperationException>();
    }

    private static ModerationProfileCollectionProcedure Procedure(bool allowSubject, bool optionalEmail = false) =>
        new(ModerationIndividualPurpose.ProfileCompletion,
            ModerationProfileCollectionApplicability.NativeIndependentlyAcceptedAssessment,
            allowSubject ? [ModerationProfilePreliminaryFieldKind.ApplicabilityDeclaration,
                ModerationProfilePreliminaryFieldKind.SubjectConsentReference]
                : [ModerationProfilePreliminaryFieldKind.ApplicabilityDeclaration],
            optionalEmail ? [ModerationProfileNormalFieldKind.DateOfBirth, ModerationProfileNormalFieldKind.Email]
                : [ModerationProfileNormalFieldKind.DateOfBirth],
            "Reviewed relationship criteria.", "Reviewed manual issuer criteria.");

    private sealed class Data
    {
        public Data(Guid? human = null, bool allowSubjectReference = true)
        {
            Human = human ?? Guid.CreateVersion7();
            var reviewer = Guid.CreateVersion7();
            var owner = Guid.CreateVersion7();
            var ownership = new ModerationChildAgePolicyOwnership(reviewer, reviewer, owner, owner,
                Guid.CreateVersion7(), "Distinct canonical controllers.", "Privacy reviewer mandate.",
                "Child safeguarding mandate.", "Native collection mandate.");
            Age = ModerationChildAgePolicySource.Record(Guid.CreateVersion7(), Guid.CreateVersion7(),
                Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, Guid.CreateVersion7(),
                Guid.CreateVersion7(), Guid.CreateVersion7(), 1, new string('a', 64), Terms(), ownership,
                ownership.GovernanceControllerHumanId, 4, Now, Now.AddMinutes(5));
            Parent = ModerationChildAgePolicyAdoption.Adopt(Guid.CreateVersion7(), Age, reviewer, 5, Now, Now.AddMinutes(4));
            Source = ModerationProfileCollectionProcedureSource.Record(Guid.CreateVersion7(), Age, Parent,
                Procedure(allowSubjectReference), owner, 6, Now, Now.AddMinutes(3));
            Adoption = ModerationProfileCollectionProcedureAdoption.Adopt(Guid.CreateVersion7(), Source, Age, Parent,
                reviewer, 7, Now, Now.AddMinutes(2));
            var instance = AuthorityScope.Instance(AuthorityScope.Solution(AuthorityScope.TrustDomain(Age.TrustDomainId),
                Guid.CreateVersion7()), Guid.CreateVersion7());
            var policy = new ModerationAgePolicy(Age.PolicyId, Age.PolicyRevision, AuthorityScope.Tenant(instance, Age.TenantId));
            Binding = new(ModerationIndividualTarget.Human(SubjectReference.Create(ModerationSubjectKind.Human, Human, instance)),
                policy, ModerationIndividualPurpose.ProfileCompletion, Guid.CreateVersion7(), 1, 50,
                Guid.CreateVersion7(), 50, Now, Now.AddMinutes(2));
            Assessment = Assess();
        }

        public Guid Human { get; }
        public ModerationChildAgePolicySource Age { get; }
        public ModerationChildAgePolicyAdoption Parent { get; }
        public ModerationProfileCollectionProcedureSource Source { get; }
        public ModerationProfileCollectionProcedureAdoption Adoption { get; }
        public ModerationIndividualBinding Binding { get; }
        public ModerationCollectionPrerequisiteAssessment Assessment { get; }

        public ModerationCollectionPrerequisiteAssessment Assess(ModerationIndividualBinding? binding = null,
            ModerationIndividualNativeReference? reference = null,
            IEnumerable<ModerationCollectionPrerequisiteKind>? required = null) =>
            new(binding ?? Binding, reference ?? new(Guid.CreateVersion7(), 1, 1, Now, Now.AddMinutes(2)),
                required ?? [ModerationCollectionPrerequisiteKind.AccountEligibility,
                    ModerationCollectionPrerequisiteKind.SubjectConsent, ModerationCollectionPrerequisiteKind.GuardianConsent]);

        public ModerationProfileSubjectConsent Grant(Guid? id = null, Guid? human = null, long generation = 1,
            ModerationIndividualBinding? binding = null, ModerationCollectionPrerequisiteAssessment? assessment = null,
            DateTimeOffset? now = null, DateTimeOffset? end = null) =>
            ModerationProfileSubjectConsent.Grant(id ?? Guid.CreateVersion7(), Source, Adoption, Age, Parent,
                binding ?? Binding, assessment ?? Assessment, human ?? Human, generation, now ?? Now, end ?? Now.AddMinutes(1));

        public ModerationIndividualBinding ChangeBinding(string change)
        {
            var original = Binding;
            var target = change switch
            {
                "subject" => ModerationIndividualTarget.Human(SubjectReference.Create(ModerationSubjectKind.Human,
                    Guid.CreateVersion7(), original.Target.Origin)),
                "guest" => ModerationIndividualTarget.RegistrationSession(original.Target.Origin, Guid.CreateVersion7(), Guid.CreateVersion7()),
                _ => original.Target
            };
            var policy = new ModerationAgePolicy(change == "policy" ? Guid.CreateVersion7() : original.PolicyId,
                change == "policy-revision" ? 2 : original.PolicyRevision,
                change switch
                {
                    "jurisdiction" => original.Target.Origin,
                    "trust-jurisdiction" => AuthorityScope.TrustDomain(original.Target.Origin.TrustDomainId),
                    "foreign-tenant" => AuthorityScope.Tenant(original.Target.Origin, Guid.CreateVersion7()),
                    _ => original.Jurisdiction
                });
            return new(target, policy, change == "purpose" ? ModerationIndividualPurpose.ParticipantCollection
                    : change == "unknown-purpose" ? (ModerationIndividualPurpose)99 : original.Purpose,
                change == "source" ? Guid.CreateVersion7() : original.SourceId,
                change == "source-revision" ? 2 : original.SourceRevision,
                change == "source-generation" ? 51 : original.SourceGeneration,
                change == "stamp" ? Guid.CreateVersion7() : original.ObservationStamp,
                change == "sequence" ? 51 : original.ObservationSequence,
                change == "observed-at" ? Now.AddTicks(-1) : original.ObservedAt,
                change == "valid-until" ? Now.AddMinutes(3)
                    : change == "short-window" ? Now.AddSeconds(30) : original.ValidUntil);
        }

        public ModerationChildAgePolicySource CopyAge(long writer, string change = "") =>
            ModerationChildAgePolicySource.Record(Age.Id,
                change == "host" ? Guid.CreateVersion7() : Age.HostBindingId,
                change == "trust" ? Guid.CreateVersion7() : Age.TrustDomainId,
                change == "tenant" ? Guid.CreateVersion7() : Age.TenantId,
                change == "policy" ? Guid.CreateVersion7() : Age.PolicyId,
                change == "policy-revision" ? 2 : Age.PolicyRevision,
                change == "publication" ? Guid.CreateVersion7() : Age.PublicationId,
                change == "definition" ? Guid.CreateVersion7() : Age.DefinitionId,
                change == "profile" ? Guid.CreateVersion7() : Age.ProfileId,
                change == "mode" ? 2 : Age.ModeCode,
                change == "digest" ? new string('b', 64) : Age.ProposedStateDigest,
                change == "terms" ? Age.Terms with { AdulthoodAge = 21 } : Age.Terms,
                Age.Ownership, Age.RecordedByHumanId, writer, Age.RecordedAt,
                change == "age-window" ? Age.ValidUntil.AddTicks(1) : Age.ValidUntil);
    }

    private static ModerationChildAgePolicyTerms Terms() => new(
        ModerationScopeKind.Tenant, ModerationChildAgeCollection.LawfulMinimumBeforeSensitiveBodyHandling,
        ModerationChildAgeConsent.ActualSubjectPurposePolicyBoundPrerequisites,
        ModerationChildAgeAssurance.QualifiedLocalManualThresholdNotDeclaredDob, ModerationChildAgeProviderAssurance.NotAccepted,
        ModerationChildAgeEvidence.UnknownContradictoryExpiredRemainRestrictedNotGuilty,
        ModerationChildAgeClassification.ReviewedContentNotEventFormat, 18,
        ModerationChildAgeAudience.WholeEventUnlessCompleteSeparatelyQualifiedSegregation,
        ModerationChildAgeParticipant.ActualViewerAndParticipantNeverPurchaser,
        ModerationChildAgeCorrection.InvalidateDerivedAgeWithoutStrikeOrRecognitionRestoration,
        ModerationChildAgeProtectedAccess.CatalogueRemedyOrdinaryErasureIndependent,
        ModerationChildAgeProhibitedInference.NoChildReligionBehaviourOrUniversalIdentityFace,
        ModerationChildAgeCalendar.Gregorian, ModerationChildAgeTimeZone.Utc, ModerationChildAgeLeapDay.MarchFirstInNonLeapYear,
        ModerationChildAgeAdulthood.ReconsiderGuardianGrantsAndPurposes, 18,
        "Child risk review.", "Privacy purpose review.", "Minimum profile collection.", "Independent prerequisite assessment.",
        "Qualified local manual review.", null, "Reviewed classification.", "Gregorian UTC review.");
}
