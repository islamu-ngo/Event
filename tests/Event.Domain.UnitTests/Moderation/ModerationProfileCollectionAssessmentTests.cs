using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationProfileCollectionAssessmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly ModerationCollectionPrerequisiteKind[] Full =
        [ModerationCollectionPrerequisiteKind.AccountEligibility, ModerationCollectionPrerequisiteKind.SubjectConsent,
            ModerationCollectionPrerequisiteKind.GuardianConsent];

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task IndependentAcceptance_ProducesOnlyOriginalAssessmentMetadata(bool full)
    {
        var data = new Data();
        var required = full ? Full : [];
        var source = data.Record(required: required);
        var adoption = data.Adopt(source);
        var assessment = adoption.ToAssessment(source, data.Procedure, data.AcceptedProcedure,
            data.Age, data.Parent, data.Binding, Now.AddSeconds(1));
        await Assert.That(assessment.Binding).IsEqualTo(data.Binding);
        await Assert.That(assessment.Required.SequenceEqual(required)).IsTrue();
        await Assert.That(assessment.Reference.Id).IsEqualTo(adoption.Id);
        await Assert.That(assessment.Reference.Revision).IsEqualTo(1L);
        await Assert.That(assessment.Reference.Generation).IsEqualTo(2L);
        await Assert.That(assessment.Reference.ObservedAt).IsEqualTo(Now);
        await Assert.That(assessment.Reference.ValidUntil).IsEqualTo(Now.AddSeconds(30));
        await Assert.That(source.Generation).IsEqualTo(1L);
        await Assert.That(source.Binding.Target.HumanSubject!.Id).IsEqualTo(data.Human);
        source.ValidatePersistedState();
        adoption.ValidatePersistedState();
        if (full)
            await Assert.That(() => ModerationCollectionReceipt.Record(Guid.CreateVersion7(), data.Binding,
                assessment, [], Now, Now.AddSeconds(20))).Throws<ArgumentException>();
    }

    [Test]
    public async Task RequiredSet_IsDefensivelySortedAndEmptyMustBeExplicit()
    {
        var data = new Data();
        var required = new List<ModerationCollectionPrerequisiteKind> { Full[2], Full[0], Full[1] };
        var source = data.Record(required: required);
        required.Clear();
        var adoption = data.Adopt(source);
        await Assert.That(source.Required.SequenceEqual(Full)).IsTrue();
        await Assert.That(adoption.ToAssessment(source, data.Procedure, data.AcceptedProcedure,
            data.Age, data.Parent, data.Binding, Now).Required.SequenceEqual(Full)).IsTrue();
        await Assert.That(() => ModerationProfileCollectionAssessmentSource.Record(Guid.CreateVersion7(),
            data.Procedure, data.AcceptedProcedure, data.Age, data.Parent, data.Binding, null!,
            data.Age.CollectionOwnerHumanId, 1, Now, Now.AddMinutes(1))).Throws<ArgumentNullException>();
        await Assert.That(source.ToString()).IsEqualTo(nameof(ModerationProfileCollectionAssessmentSource));
        await Assert.That(adoption.ToString()).IsEqualTo(nameof(ModerationProfileCollectionAssessmentAdoption));
    }

    [Test]
    [Arguments("unknown")]
    [Arguments("duplicate")]
    [Arguments("overflow")]
    [Arguments("account-field")]
    [Arguments("subject-field")]
    [Arguments("guardian-consent-field")]
    [Arguments("guardian-relationship-field")]
    public async Task Source_RejectsUnknownSetsAndInsufficientSeparateReferenceVocabulary(string change)
    {
        var missing = change switch
        {
            "account-field" => ModerationProfilePreliminaryFieldKind.AccountEligibilityReference,
            "subject-field" => ModerationProfilePreliminaryFieldKind.SubjectConsentReference,
            "guardian-consent-field" => ModerationProfilePreliminaryFieldKind.GuardianConsentReference,
            "guardian-relationship-field" => ModerationProfilePreliminaryFieldKind.GuardianRelationshipReference,
            _ => (ModerationProfilePreliminaryFieldKind?)null
        };
        var data = new Data(missing: missing);
        var required = change switch
        {
            "unknown" => [(ModerationCollectionPrerequisiteKind)99],
            "duplicate" => new[] { Full[0], Full[0] },
            "overflow" => new[] { Full[0], Full[1], Full[2], Full[0] },
            _ => Full
        };
        await Assert.That(() => data.Record(required: required)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("guest")]
    [Arguments("purpose")]
    [Arguments("policy")]
    [Arguments("policy-revision")]
    [Arguments("trust-scope")]
    [Arguments("resource-scope")]
    [Arguments("foreign-tenant")]
    [Arguments("foreign-trust")]
    public async Task Source_RequiresActualHumanProfilePurposeAndConcreteProcedurePolicyScope(string change)
    {
        var data = new Data();
        await Assert.That(() => data.Record(binding: data.ChangeBinding(change))).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("v4-id")]
    [Arguments("procedure-id")]
    [Arguments("procedure-adoption-id")]
    [Arguments("parent-id")]
    [Arguments("policy-id")]
    [Arguments("native-source-id")]
    [Arguments("observation-id")]
    [Arguments("subject-id")]
    [Arguments("recorder")]
    [Arguments("zero-generation")]
    [Arguments("max-generation")]
    public async Task Source_RequiresDistinctAggregateAndNominatedOwnerWithOwnFiniteGeneration(string change)
    {
        var data = new Data();
        var id = change switch
        {
            "v4-id" => Guid.NewGuid(),
            "procedure-id" => data.Procedure.Id,
            "procedure-adoption-id" => data.AcceptedProcedure.Id,
            "parent-id" => data.Parent.Id,
            "policy-id" => data.Binding.PolicyId,
            "native-source-id" => data.Binding.SourceId,
            "observation-id" => data.Binding.ObservationStamp,
            "subject-id" => data.Human,
            _ => Guid.CreateVersion7()
        };
        await Assert.That(() => data.Record(id: id,
            human: change == "recorder" ? data.Age.ReviewerHumanId : null,
            generation: change == "zero-generation" ? 0 : change == "max-generation" ? long.MaxValue : 1))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments("empty")]
    [Arguments("infinite")]
    [Arguments("non-utc")]
    [Arguments("before-binding")]
    [Arguments("binding-expiry")]
    [Arguments("procedure-expiry")]
    [Arguments("withdrawn-procedure")]
    [Arguments("withdrawn-parent")]
    public async Task Source_WindowMustFitLiveBindingAndEveryOriginalParent(string change)
    {
        var data = new Data();
        if (change == "withdrawn-procedure") data.AcceptedProcedure.Withdraw(1, data.Age.ReviewerHumanId, Now);
        if (change == "withdrawn-parent") data.Parent.Withdraw(1, data.Age.ReviewerHumanId, Now);
        var now = change == "before-binding" ? Now.AddTicks(-1)
            : change == "non-utc" ? Now.ToOffset(TimeSpan.FromHours(1)) : Now;
        var end = change == "empty" ? Now : change == "infinite" ? DateTimeOffset.MaxValue
            : change == "procedure-expiry" ? data.AcceptedProcedure.ValidUntil.AddTicks(1) : Now.AddMinutes(1);
        await Assert.That(() => data.Record(now: now, end: end,
            binding: change == "binding-expiry" ? data.ChangeBinding("short-window") : null)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("recorder")]
    [Arguments("unrelated")]
    [Arguments("subject-reviewer")]
    [Arguments("subject-controller")]
    [Arguments("same-generation")]
    [Arguments("max-generation")]
    [Arguments("source-id")]
    [Arguments("v4-id")]
    [Arguments("long-window")]
    public async Task Adoption_RequiresNominatedIndependentReviewerAndNewerSameSlotGeneration(string change)
    {
        var data = new Data(subjectRole: change);
        var source = data.Record();
        await Assert.That(() => data.Adopt(source,
            human: change == "recorder" ? data.Age.CollectionOwnerHumanId
                : change == "unrelated" ? Guid.CreateVersion7() : null,
            generation: change == "same-generation" ? 1 : change == "max-generation" ? long.MaxValue : 2,
            id: change == "source-id" ? source.Id : change == "v4-id" ? Guid.NewGuid() : null,
            end: change == "long-window" ? source.ValidUntil.AddTicks(1) : null)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("subject")]
    [Arguments("purpose")]
    [Arguments("policy")]
    [Arguments("policy-revision")]
    [Arguments("source")]
    [Arguments("source-revision")]
    [Arguments("source-generation")]
    [Arguments("stamp")]
    [Arguments("sequence")]
    [Arguments("observed-at")]
    [Arguments("valid-until")]
    [Arguments("jurisdiction")]
    public async Task Acceptance_CannotRebindAnyIndividualObservationCoordinate(string change)
    {
        var data = new Data();
        var source = data.Record();
        var adoption = data.Adopt(source);
        var binding = data.ChangeBinding(change);
        await Assert.That(adoption.MatchesCurrentMetadata(source, data.Procedure, data.AcceptedProcedure,
            data.Age, data.Parent, binding, Now)).IsFalse();
        await Assert.That(() => adoption.ToAssessment(source, data.Procedure, data.AcceptedProcedure,
            data.Age, data.Parent, binding, Now)).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("id")]
    [Arguments("generation")]
    [Arguments("required")]
    [Arguments("lease")]
    [Arguments("recorded-at")]
    public async Task Acceptance_RejectsAssessmentSourceReplacementEvenWithSameOriginalProcedure(string change)
    {
        var data = new Data();
        var source = data.Record();
        var adoption = data.Adopt(source);
        var replacement = data.Record(id: change == "id" ? Guid.CreateVersion7() : source.Id,
            generation: change == "generation" ? 2 : 1,
            required: change == "required" ? [] : Full,
            now: change == "recorded-at" ? Now.AddTicks(1) : Now,
            end: change == "lease" ? source.ValidUntil.AddTicks(-1) : source.ValidUntil);
        await Assert.That(adoption.MatchesCurrentMetadata(replacement, data.Procedure, data.AcceptedProcedure,
            data.Age, data.Parent, data.Binding, Now.AddSeconds(1))).IsFalse();
        await Assert.That(() => adoption.ToAssessment(replacement, data.Procedure, data.AcceptedProcedure,
            data.Age, data.Parent, data.Binding, Now.AddSeconds(1))).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("procedure")]
    [Arguments("writer")]
    [Arguments("adoption-id")]
    [Arguments("adoption-writer")]
    [Arguments("adoption-window")]
    [Arguments("parent-writer")]
    [Arguments("parent-window")]
    [Arguments("parent-withdrawal")]
    [Arguments("procedure-withdrawal")]
    public async Task Acceptance_RejectsProcedureAndParentSubstitutionsAndCannotReadoptThem(string change)
    {
        var data = new Data();
        var source = data.Record();
        var adoption = data.Adopt(source);
        var procedure = data.Procedure;
        var accepted = data.AcceptedProcedure;
        var parent = data.Parent;
        if (change is "procedure" or "writer")
            procedure = ModerationProfileCollectionProcedureSource.Record(procedure.Id, data.Age, parent,
                change == "procedure" ? Procedure(optionalEmail: true) : procedure.Procedure,
                data.Age.CollectionOwnerHumanId, change == "writer" ? 8 : 6, Now, procedure.ValidUntil);
        if (change is "adoption-id" or "adoption-writer" or "adoption-window")
            accepted = ModerationProfileCollectionProcedureAdoption.Adopt(
                change == "adoption-id" ? Guid.CreateVersion7() : accepted.Id, procedure, data.Age, parent,
                data.Age.ReviewerHumanId, change == "adoption-writer" ? 8 : 7, Now,
                change == "adoption-window" ? accepted.ValidUntil.AddTicks(-1) : accepted.ValidUntil);
        if (change is "parent-writer" or "parent-window")
            parent = ModerationChildAgePolicyAdoption.Adopt(parent.Id, data.Age, data.Age.ReviewerHumanId,
                change == "parent-writer" ? 6 : 5, Now,
                change == "parent-window" ? parent.ValidUntil.AddTicks(-1) : parent.ValidUntil);
        if (change == "parent-withdrawal") parent.Withdraw(1, data.Age.ReviewerHumanId, Now);
        if (change == "procedure-withdrawal") accepted.Withdraw(1, data.Age.ReviewerHumanId, Now);
        await Assert.That(adoption.MatchesCurrentMetadata(source, procedure, accepted,
            data.Age, parent, data.Binding, Now)).IsFalse();
        await Assert.That(() => ModerationProfileCollectionAssessmentAdoption.Adopt(Guid.CreateVersion7(), source,
            procedure, accepted, data.Age, parent, data.Binding, data.Age.ReviewerHumanId,
            2, Now, Now.AddSeconds(30))).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("host")]
    [Arguments("trust")]
    [Arguments("tenant")]
    [Arguments("policy")]
    [Arguments("policy-revision")]
    [Arguments("publication")]
    [Arguments("profile")]
    [Arguments("terms")]
    [Arguments("writer")]
    [Arguments("lease")]
    public async Task Acceptance_RequiresAllOriginalChildAgeCoordinates(string change)
    {
        var data = new Data();
        var source = data.Record();
        var adoption = data.Adopt(source);
        var age = data.CopyAge(change);
        await Assert.That(adoption.MatchesCurrentMetadata(source, data.Procedure, data.AcceptedProcedure,
            age, data.Parent, data.Binding, Now)).IsFalse();
        await Assert.That(() => ModerationProfileCollectionAssessmentAdoption.Adopt(Guid.CreateVersion7(), source,
            data.Procedure, data.AcceptedProcedure, age, data.Parent, data.Binding, data.Age.ReviewerHumanId,
            2, Now, Now.AddSeconds(30))).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("source-revision")]
    [Arguments("source-recorder")]
    [Arguments("procedure-source-id")]
    [Arguments("subject-human-id")]
    [Arguments("owner-controller-id")]
    [Arguments("reviewer-controller-id")]
    [Arguments("procedure-adoption-id")]
    [Arguments("procedure-adoption-revision")]
    [Arguments("required")]
    [Arguments("adoption-source-id")]
    [Arguments("adoption-source-revision")]
    [Arguments("adoption-source-generation")]
    [Arguments("adoption-reviewer")]
    [Arguments("adoption-generation")]
    [Arguments("withdrawal")]
    public async Task MaterializedOriginals_RejectCorruptCoordinatesAndTerminalState(string change)
    {
        var data = new Data();
        var source = data.Record();
        var adoption = data.Adopt(source);
        switch (change)
        {
            case "source-revision":
                typeof(ModerationProfileCollectionAssessmentSource).GetProperty(nameof(source.Revision))!
                    .SetValue(source, 2L);
                break;
            case "source-recorder":
                typeof(ModerationProfileCollectionAssessmentSource).GetProperty(nameof(source.RecordedByHumanId))!
                    .SetValue(source, data.Age.ReviewerHumanId);
                break;
            case "procedure-source-id":
                typeof(ModerationProfileCollectionAssessmentSource).GetProperty(nameof(source.ProcedureSourceId))!
                    .SetValue(source, data.Age.Id);
                break;
            case "subject-human-id":
                typeof(ModerationProfileCollectionAssessmentSource).GetProperty(nameof(source.SubjectHumanId))!
                    .SetValue(source, data.Age.ReviewerHumanId);
                break;
            case "owner-controller-id":
                typeof(ModerationProfileCollectionAssessmentSource).GetProperty(nameof(source.OwnerControllerHumanId))!
                    .SetValue(source, data.Age.CollectionOwnerHumanId);
                break;
            case "reviewer-controller-id":
                typeof(ModerationProfileCollectionAssessmentSource).GetProperty(nameof(source.ReviewerControllerHumanId))!
                    .SetValue(source, data.Age.ReviewerHumanId);
                break;
            case "procedure-adoption-id":
                typeof(ModerationProfileCollectionAssessmentSource).GetProperty(nameof(source.ProcedureAdoptionId))!
                    .SetValue(source, data.Age.Id);
                break;
            case "procedure-adoption-revision":
                typeof(ModerationProfileCollectionAssessmentSource).GetProperty(nameof(source.ProcedureAdoptionRevision))!
                    .SetValue(source, 2L);
                break;
            case "required":
                typeof(ModerationProfileCollectionAssessmentSource).GetProperty(nameof(source.Required))!
                    .SetValue(source, new[] { Full[1], Full[1] });
                break;
            case "adoption-source-id":
                typeof(ModerationProfileCollectionAssessmentAdoption).GetProperty(nameof(adoption.SourceId))!
                    .SetValue(adoption, Guid.CreateVersion7());
                break;
            case "adoption-source-revision":
                typeof(ModerationProfileCollectionAssessmentAdoption).GetProperty(nameof(adoption.SourceRevision))!
                    .SetValue(adoption, 2L);
                break;
            case "adoption-source-generation":
                typeof(ModerationProfileCollectionAssessmentAdoption).GetProperty(nameof(adoption.SourceGeneration))!
                    .SetValue(adoption, 2L);
                break;
            case "adoption-reviewer":
                typeof(ModerationProfileCollectionAssessmentAdoption).GetProperty(nameof(adoption.AdoptedByHumanId))!
                    .SetValue(adoption, data.Age.CollectionOwnerHumanId);
                break;
            case "adoption-generation":
                typeof(ModerationProfileCollectionAssessmentAdoption).GetProperty(nameof(adoption.Generation))!
                    .SetValue(adoption, 1L);
                break;
            case "withdrawal":
                typeof(ModerationProfileCollectionAssessmentAdoption).GetProperty(nameof(adoption.Revision))!
                    .SetValue(adoption, 2L);
                break;
        }
        await Assert.That(() => adoption.ValidatePersistedState()).Throws<ArgumentException>();
    }

    [Test]
    public async Task ExactClonesAndV4HumanAreSupportedWithoutComparingGlobalWriterSequences()
    {
        var data = new Data();
        var source = data.Record();
        var adoption = data.Adopt(source);
        var binding = data.ChangeBinding("");
        var clone = data.Record(id: source.Id, binding: binding, required: Full.Reverse());
        await Assert.That(data.Human.Version).IsEqualTo(4);
        await Assert.That(adoption.MatchesCurrentMetadata(clone, data.Procedure, data.AcceptedProcedure,
            data.Age, data.Parent, binding, Now)).IsTrue();
        await Assert.That(adoption.Generation).IsEqualTo(2L);
        await Assert.That(data.AcceptedProcedure.WriterGeneration).IsEqualTo(7L);
        await Assert.That(data.Binding.SourceGeneration).IsEqualTo(50L);
    }

    [Test]
    [Arguments(-1, false)]
    [Arguments(0, true)]
    [Arguments(29, true)]
    [Arguments(30, false)]
    public async Task OriginalAcceptedWindowCannotBeRenewed(int seconds, bool expected)
    {
        var data = new Data();
        var source = data.Record();
        var adoption = data.Adopt(source);
        await Assert.That(adoption.MatchesCurrentMetadata(source, data.Procedure, data.AcceptedProcedure,
            data.Age, data.Parent, data.Binding, Now.AddSeconds(seconds))).IsEqualTo(expected);
    }

    [Test]
    [Arguments("reviewer")]
    [Arguments("reviewer-controller")]
    [Arguments("owner")]
    [Arguments("owner-controller")]
    public async Task Withdrawal_IsNominatedTerminalRevisionCasAfterAllParentsExpire(string role)
    {
        var data = new Data();
        var source = data.Record();
        var adoption = data.Adopt(source);
        await Assert.That(() => adoption.Withdraw(2, data.Age.ReviewerHumanId, Now)).Throws<InvalidOperationException>();
        await Assert.That(() => adoption.Withdraw(1, data.Human, Now)).Throws<ArgumentException>();
        await Assert.That(() => adoption.Withdraw(1, Guid.Empty, Now)).Throws<ArgumentException>();
        await Assert.That(() => adoption.Withdraw(1, data.Age.ReviewerHumanId, Now.AddTicks(-1))).Throws<ArgumentException>();
        await Assert.That(() => adoption.Withdraw(1, data.Age.ReviewerHumanId, DateTimeOffset.MaxValue))
            .Throws<ArgumentException>();
        await Assert.That(() => adoption.Withdraw(1, data.Age.ReviewerHumanId, Now.ToOffset(TimeSpan.FromHours(1))))
            .Throws<ArgumentException>();
        data.Parent.Withdraw(1, data.Age.ReviewerHumanId, Now);
        data.AcceptedProcedure.Withdraw(1, data.Age.ReviewerHumanId, Now);
        var human = role switch
        {
            "owner" => data.Age.CollectionOwnerHumanId,
            "owner-controller" => data.Age.CollectionControllerHumanId,
            "reviewer-controller" => data.Age.ReviewerControllerHumanId,
            _ => data.Age.ReviewerHumanId
        };
        adoption.Withdraw(1, human, Now.AddDays(1));
        adoption.ValidatePersistedState();
        await Assert.That(adoption.Revision).IsEqualTo(2L);
        await Assert.That(adoption.WithdrawnByHumanId).IsEqualTo(human);
        await Assert.That(adoption.WithdrawnAt).IsEqualTo(Now.AddDays(1));
        await Assert.That(adoption.AdoptedAt).IsEqualTo(Now);
        await Assert.That(adoption.ValidUntil).IsEqualTo(Now.AddSeconds(30));
        await Assert.That(adoption.Generation).IsEqualTo(2L);
        await Assert.That(adoption.MatchesCurrentMetadata(source, data.Procedure, data.AcceptedProcedure,
            data.Age, data.Parent, data.Binding, Now)).IsFalse();
        await Assert.That(() => adoption.ToAssessment(source, data.Procedure, data.AcceptedProcedure,
            data.Age, data.Parent, data.Binding, Now)).Throws<InvalidOperationException>();
        await Assert.That(() => adoption.Withdraw(2, human, Now.AddDays(2))).Throws<InvalidOperationException>();
    }

    private static ModerationProfileCollectionProcedure Procedure(
        ModerationProfilePreliminaryFieldKind? missing = null, bool optionalEmail = false) =>
        new(ModerationIndividualPurpose.ProfileCompletion,
            ModerationProfileCollectionApplicability.NativeIndependentlyAcceptedAssessment,
            Enum.GetValues<ModerationProfilePreliminaryFieldKind>().Where(kind => kind != missing).ToArray(),
            optionalEmail ? [ModerationProfileNormalFieldKind.DateOfBirth, ModerationProfileNormalFieldKind.Email]
                : [ModerationProfileNormalFieldKind.DateOfBirth],
            "Reviewed separate relationship criteria.", "Reviewed manual issuer criteria.");

    private sealed class Data
    {
        public Data(ModerationProfilePreliminaryFieldKind? missing = null, string subjectRole = "")
        {
            var reviewer = Guid.NewGuid();
            var reviewerController = Guid.NewGuid();
            var owner = Guid.NewGuid();
            var ownerController = Guid.NewGuid();
            Human = subjectRole == "subject-reviewer" ? reviewer
                : subjectRole == "subject-controller" ? reviewerController : Guid.NewGuid();
            var ownership = new ModerationChildAgePolicyOwnership(reviewer, reviewerController, owner, ownerController,
                Guid.NewGuid(), "Distinct canonical controllers.", "Privacy reviewer mandate.",
                "Child safeguarding mandate.", "Native collection mandate.");
            Age = ModerationChildAgePolicySource.Record(Guid.CreateVersion7(), Guid.CreateVersion7(),
                Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, Guid.CreateVersion7(),
                Guid.CreateVersion7(), Guid.CreateVersion7(), 1, new string('a', 64), Terms(), ownership,
                ownership.GovernanceControllerHumanId, 4, Now, Now.AddMinutes(5));
            Parent = ModerationChildAgePolicyAdoption.Adopt(Guid.CreateVersion7(), Age, reviewer, 5, Now, Now.AddMinutes(4));
            Procedure = ModerationProfileCollectionProcedureSource.Record(Guid.CreateVersion7(), Age, Parent,
                ModerationProfileCollectionAssessmentTests.Procedure(missing), owner, 6, Now, Now.AddMinutes(3));
            AcceptedProcedure = ModerationProfileCollectionProcedureAdoption.Adopt(Guid.CreateVersion7(),
                Procedure, Age, Parent, reviewer, 7, Now, Now.AddMinutes(2));
            var instance = AuthorityScope.Instance(AuthorityScope.Solution(AuthorityScope.TrustDomain(Age.TrustDomainId),
                Guid.CreateVersion7()), Guid.CreateVersion7());
            Binding = new(ModerationIndividualTarget.Human(SubjectReference.Create(ModerationSubjectKind.Human, Human, instance)),
                new(Age.PolicyId, Age.PolicyRevision, AuthorityScope.Tenant(instance, Age.TenantId)),
                ModerationIndividualPurpose.ProfileCompletion, Guid.CreateVersion7(), 1, 50,
                Guid.CreateVersion7(), 50, Now, Now.AddMinutes(2));
        }

        public Guid Human { get; }
        public ModerationChildAgePolicySource Age { get; }
        public ModerationChildAgePolicyAdoption Parent { get; }
        public ModerationProfileCollectionProcedureSource Procedure { get; }
        public ModerationProfileCollectionProcedureAdoption AcceptedProcedure { get; }
        public ModerationIndividualBinding Binding { get; }

        public ModerationProfileCollectionAssessmentSource Record(Guid? id = null, Guid? human = null,
            long generation = 1, ModerationIndividualBinding? binding = null,
            IEnumerable<ModerationCollectionPrerequisiteKind>? required = null,
            DateTimeOffset? now = null, DateTimeOffset? end = null) =>
            ModerationProfileCollectionAssessmentSource.Record(id ?? Guid.CreateVersion7(),
                Procedure, AcceptedProcedure, Age, Parent, binding ?? Binding, required ?? Full,
                human ?? Age.CollectionOwnerHumanId, generation, now ?? Now, end ?? Now.AddMinutes(1));

        public ModerationProfileCollectionAssessmentAdoption Adopt(ModerationProfileCollectionAssessmentSource source,
            Guid? id = null, Guid? human = null, long generation = 2, DateTimeOffset? end = null) =>
            ModerationProfileCollectionAssessmentAdoption.Adopt(id ?? Guid.CreateVersion7(), source,
                Procedure, AcceptedProcedure, Age, Parent, Binding, human ?? Age.ReviewerHumanId,
                generation, Now, end ?? Now.AddSeconds(30));

        public ModerationIndividualBinding ChangeBinding(string change)
        {
            var original = Binding;
            var origin = change == "foreign-trust"
                ? AuthorityScope.Instance(AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()),
                    Guid.CreateVersion7()), Guid.CreateVersion7()) : original.Target.Origin;
            var target = change == "guest"
                ? ModerationIndividualTarget.RegistrationSession(origin, Guid.CreateVersion7(), Guid.CreateVersion7())
                : ModerationIndividualTarget.Human(SubjectReference.Create(ModerationSubjectKind.Human,
                    change == "subject" ? Guid.NewGuid() : Human, origin));
            var tenant = AuthorityScope.Tenant(origin, change == "foreign-tenant" ? Guid.CreateVersion7() : Age.TenantId);
            var scope = change switch
            {
                "trust-scope" => AuthorityScope.TrustDomain(Age.TrustDomainId),
                "resource-scope" => AuthorityScope.Resource(tenant, Guid.CreateVersion7()),
                "foreign-tenant" or "foreign-trust" => tenant,
                "jurisdiction" => origin,
                _ => original.Jurisdiction
            };
            return new(target, new(change == "policy" ? Guid.CreateVersion7() : original.PolicyId,
                    change == "policy-revision" ? 2 : original.PolicyRevision, scope),
                change == "purpose" ? ModerationIndividualPurpose.ParticipantCollection : original.Purpose,
                change == "source" ? Guid.CreateVersion7() : original.SourceId,
                change == "source-revision" ? 2 : original.SourceRevision,
                change == "source-generation" ? 51 : original.SourceGeneration,
                change == "stamp" ? Guid.CreateVersion7() : original.ObservationStamp,
                change == "sequence" ? 51 : original.ObservationSequence,
                change == "observed-at" ? Now.AddTicks(-1) : original.ObservedAt,
                change == "valid-until" ? Now.AddMinutes(3)
                    : change == "short-window" ? Now.AddSeconds(30) : original.ValidUntil);
        }

        public ModerationChildAgePolicySource CopyAge(string change) =>
            ModerationChildAgePolicySource.Record(Age.Id,
                change == "host" ? Guid.CreateVersion7() : Age.HostBindingId,
                change == "trust" ? Guid.CreateVersion7() : Age.TrustDomainId,
                change == "tenant" ? Guid.CreateVersion7() : Age.TenantId,
                change == "policy" ? Guid.CreateVersion7() : Age.PolicyId,
                change == "policy-revision" ? 2 : Age.PolicyRevision,
                change == "publication" ? Guid.CreateVersion7() : Age.PublicationId,
                Age.DefinitionId, change == "profile" ? Guid.CreateVersion7() : Age.ProfileId,
                Age.ModeCode, Age.ProposedStateDigest,
                change == "terms" ? Age.Terms with { AdulthoodAge = 21 } : Age.Terms,
                Age.Ownership, Age.RecordedByHumanId, change == "writer" ? 5 : Age.WriterGeneration,
                Age.RecordedAt, change == "lease" ? Age.ValidUntil.AddTicks(-1) : Age.ValidUntil);
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
