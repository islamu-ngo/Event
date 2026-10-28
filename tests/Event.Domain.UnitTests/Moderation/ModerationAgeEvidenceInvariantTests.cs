using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationAgeEvidenceInvariantTests
{
    [Test]
    public async Task Declaration_never_becomes_assurance_and_unknown_never_implies_guilt()
    {
        var binding = IndividualEvidenceData.Binding();
        var declared = IndividualEvidenceData.Age(binding);
        var unknown = ModerationAgeEvidence.Record(Guid.CreateVersion7(), binding, 18,
            ModerationAgeEvidenceKind.Unknown, ModerationDeclaredAgeOutcome.Unknown, null,
            IndividualEvidenceData.Now, IndividualEvidenceData.End);

        await Assert.That(declared.EvaluateMetadata(binding, null, IndividualEvidenceData.Now).Kind)
            .IsEqualTo(ModerationAgeEvidenceKind.Declared);
        await Assert.That(unknown.EvaluateMetadata(binding, null, IndividualEvidenceData.Now).Outcome)
            .IsEqualTo(ModerationDeclaredAgeOutcome.Unknown);
    }

    [Test]
    [Arguments(-1, ModerationAgeEvidenceKind.Unknown)]
    [Arguments(0, ModerationAgeEvidenceKind.Assured)]
    [Arguments(9, ModerationAgeEvidenceKind.Assured)]
    [Arguments(10, ModerationAgeEvidenceKind.Unknown)]
    public async Task Qualification_is_half_open_and_cannot_apply_before_recording(
        int minutes, ModerationAgeEvidenceKind expected)
    {
        var binding = IndividualEvidenceData.Binding();
        var provenance = IndividualEvidenceData.Assurance(binding);
        var evidence = IndividualEvidenceData.Age(binding, provenance);
        await Assert.That(evidence.EvaluateMetadata(binding, provenance,
            IndividualEvidenceData.Now.AddMinutes(minutes)).Kind).IsEqualTo(expected);
    }

    [Test]
    [Arguments("subject")]
    [Arguments("policy")]
    [Arguments("revision")]
    [Arguments("purpose")]
    [Arguments("source")]
    [Arguments("source-revision")]
    [Arguments("generation")]
    [Arguments("stamp")]
    [Arguments("sequence")]
    [Arguments("jurisdiction")]
    public async Task Changed_native_coordinates_cannot_reuse_age_metadata(string change)
    {
        var binding = IndividualEvidenceData.Binding();
        var evidence = IndividualEvidenceData.Age(binding);
        var changed = IndividualEvidenceData.Change(binding, change);
        await Assert.That(evidence.EvaluateMetadata(changed, null, IndividualEvidenceData.Now).Kind)
            .IsEqualTo(ModerationAgeEvidenceKind.Unknown);
    }

    [Test]
    public async Task Session_participants_and_canonical_humans_are_not_interchangeable()
    {
        var scope = IndividualEvidenceData.Instance;
        var session = Guid.CreateVersion7();
        var individual = Guid.CreateVersion7();
        var guest = ModerationIndividualTarget.RegistrationSession(scope, session, individual);
        var binding = IndividualEvidenceData.Binding(guest);
        var evidence = IndividualEvidenceData.Age(binding);
        var companion = IndividualEvidenceData.Change(binding, target:
            ModerationIndividualTarget.RegistrationSession(scope, session, Guid.CreateVersion7()));
        var differentSession = IndividualEvidenceData.Change(binding, target:
            ModerationIndividualTarget.RegistrationSession(scope, Guid.CreateVersion7(), individual));
        var purchaser = IndividualEvidenceData.Change(binding, target:
            ModerationIndividualTarget.Human(IndividualEvidenceData.Human(individual)));
        await Assert.That(evidence.EvaluateMetadata(companion, null, IndividualEvidenceData.Now).Kind)
            .IsEqualTo(ModerationAgeEvidenceKind.Unknown);
        await Assert.That(evidence.EvaluateMetadata(differentSession, null, IndividualEvidenceData.Now).Kind)
            .IsEqualTo(ModerationAgeEvidenceKind.Unknown);
        await Assert.That(evidence.EvaluateMetadata(purchaser, null, IndividualEvidenceData.Now).Kind)
            .IsEqualTo(ModerationAgeEvidenceKind.Unknown);
    }

    [Test]
    [Arguments("subject-issuer")]
    [Arguments("self-qualification")]
    [Arguments("duplicate-controller")]
    [Arguments("subject-controller")]
    [Arguments("cross-controller")]
    public async Task Known_self_attestation_and_controller_aliases_cannot_supply_manual_provenance(string change)
    {
        var binding = IndividualEvidenceData.Binding();
        var issuer = IndividualEvidenceData.Human();
        var issuerController = IndividualEvidenceData.Human();
        var qualifier = IndividualEvidenceData.Human();
        var qualifierController = IndividualEvidenceData.Human();
        var subjectController = IndividualEvidenceData.Human();
        if (change == "subject-issuer") issuer = binding.Target.HumanSubject!;
        if (change == "self-qualification") qualifier = issuer;
        if (change == "duplicate-controller") qualifierController = issuerController;
        if (change == "subject-controller") subjectController = issuerController;
        if (change == "cross-controller") qualifierController = issuer;
        await Assert.That(() => new ModerationAgeAssuranceProvenance(binding,
            ModerationAgeAssuranceMethod.LocalQualifiedManual, issuer, issuerController,
            qualifier, qualifierController, subjectController,
            IndividualEvidenceData.Reference(), IndividualEvidenceData.Reference()))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task A_replaced_or_expired_native_qualification_cannot_reuse_assurance()
    {
        var binding = IndividualEvidenceData.Binding();
        var provenance = IndividualEvidenceData.Assurance(binding);
        var evidence = IndividualEvidenceData.Age(binding, provenance);
        var changed = new ModerationAgeAssuranceProvenance(binding, provenance.Method,
            provenance.Issuer, provenance.IssuerController, provenance.QualifiedBy,
            provenance.QualifierController, provenance.SubjectController,
            provenance.IdentityReference, IndividualEvidenceData.Reference());
        await Assert.That(evidence.EvaluateMetadata(binding, changed, IndividualEvidenceData.Now).Kind)
            .IsEqualTo(ModerationAgeEvidenceKind.Unknown);
        await Assert.That(evidence.EvaluateMetadata(binding, null, IndividualEvidenceData.Now).Kind)
            .IsEqualTo(ModerationAgeEvidenceKind.Unknown);
        await Assert.That(() => ModerationAgeEvidence.Record(Guid.CreateVersion7(), binding, 18,
            ModerationAgeEvidenceKind.Assured, ModerationDeclaredAgeOutcome.AtLeastThreshold,
            provenance, IndividualEvidenceData.Now, IndividualEvidenceData.End.AddTicks(1)))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(ModerationAgeAssuranceMethod.UnsupportedVendorClaim)]
    [Arguments((ModerationAgeAssuranceMethod)0)]
    [Arguments((ModerationAgeAssuranceMethod)99)]
    public async Task Vendor_claims_and_default_method_are_not_local_qualification(ModerationAgeAssuranceMethod method)
    {
        var binding = IndividualEvidenceData.Binding();
        await Assert.That(() => new ModerationAgeAssuranceProvenance(binding, method,
            IndividualEvidenceData.Human(), IndividualEvidenceData.Human(),
            IndividualEvidenceData.Human(), IndividualEvidenceData.Human(),
            IndividualEvidenceData.Human(), IndividualEvidenceData.Reference(),
            IndividualEvidenceData.Reference())).Throws<ArgumentException>();
    }

    [Test]
    [Arguments((ModerationAgeEvidenceKind)0, ModerationDeclaredAgeOutcome.AtLeastThreshold)]
    [Arguments(ModerationAgeEvidenceKind.Unknown, ModerationDeclaredAgeOutcome.AtLeastThreshold)]
    [Arguments(ModerationAgeEvidenceKind.Declared, ModerationDeclaredAgeOutcome.Unknown)]
    [Arguments(ModerationAgeEvidenceKind.Declared, (ModerationDeclaredAgeOutcome)0)]
    [Arguments(ModerationAgeEvidenceKind.Assured, ModerationDeclaredAgeOutcome.AtLeastThreshold)]
    public async Task Contradictory_or_default_evidence_shapes_fail_closed(
        ModerationAgeEvidenceKind kind, ModerationDeclaredAgeOutcome outcome)
    {
        await Assert.That(() => ModerationAgeEvidence.Record(Guid.CreateVersion7(),
            IndividualEvidenceData.Binding(), 18, kind, outcome, null,
            IndividualEvidenceData.Now, IndividualEvidenceData.End)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(ModerationIndividualRecordState.Corrected)]
    [Arguments(ModerationIndividualRecordState.Withdrawn)]
    [Arguments(ModerationIndividualRecordState.Expired)]
    [Arguments(ModerationIndividualRecordState.Erased)]
    public async Task Retirement_removes_current_and_retrospective_qualification_without_new_birth_information(
        ModerationIndividualRecordState state)
    {
        var binding = IndividualEvidenceData.Binding();
        var provenance = IndividualEvidenceData.Assurance(binding);
        var evidence = IndividualEvidenceData.Age(binding, provenance);
        var now = state == ModerationIndividualRecordState.Expired
            ? IndividualEvidenceData.End : IndividualEvidenceData.Now.AddMinutes(1);
        evidence.Retire(1, state, now);
        await Assert.That(evidence.EvaluateMetadata(binding, provenance, now).Kind)
            .IsEqualTo(ModerationAgeEvidenceKind.Unknown);
        await Assert.That(evidence.EvaluateMetadata(binding, provenance, IndividualEvidenceData.Now).Kind)
            .IsEqualTo(ModerationAgeEvidenceKind.Unknown);
        await Assert.That(evidence.Binding).IsNull();
        await Assert.That(evidence.Provenance).IsNull();
        await Assert.That(evidence.ValidUntil).IsEqualTo(IndividualEvidenceData.End);
        await Assert.That(evidence.Revision).IsEqualTo(2);
    }

    [Test]
    public async Task Correction_is_terminal_but_does_not_prevent_later_erasure()
    {
        var evidence = IndividualEvidenceData.Age(IndividualEvidenceData.Binding());
        await Assert.That(() => evidence.Retire(2, ModerationIndividualRecordState.Corrected,
            IndividualEvidenceData.Now)).Throws<InvalidOperationException>();
        await Assert.That(() => evidence.Retire(1, ModerationIndividualRecordState.Expired,
            IndividualEvidenceData.Now)).Throws<InvalidOperationException>();
        evidence.Retire(1, ModerationIndividualRecordState.Corrected, IndividualEvidenceData.Now);
        await Assert.That(() => evidence.Retire(2, ModerationIndividualRecordState.Current,
            IndividualEvidenceData.Now)).Throws<InvalidOperationException>();
        evidence.Retire(2, ModerationIndividualRecordState.Erased, IndividualEvidenceData.Now.AddMinutes(1));
        await Assert.That(evidence.State).IsEqualTo(ModerationIndividualRecordState.Erased);
        await Assert.That(evidence.Revision).IsEqualTo(3);
    }

    [Test]
    public async Task Last_tick_qualifies_but_exact_expiry_discards_the_threshold_outcome()
    {
        var binding = IndividualEvidenceData.Binding();
        var provenance = IndividualEvidenceData.Assurance(binding);
        var evidence = IndividualEvidenceData.Age(binding, provenance);
        await Assert.That(evidence.EvaluateMetadata(binding, provenance, IndividualEvidenceData.End.AddTicks(-1)).Kind)
            .IsEqualTo(ModerationAgeEvidenceKind.Assured);
        await Assert.That(evidence.EvaluateMetadata(binding, provenance, IndividualEvidenceData.End).Outcome)
            .IsEqualTo(ModerationDeclaredAgeOutcome.Unknown);
    }

    [Test]
    public async Task Declaration_cannot_attach_manual_provenance_to_promote_itself()
    {
        var binding = IndividualEvidenceData.Binding();
        var provenance = IndividualEvidenceData.Assurance(binding);
        var declaration = IndividualEvidenceData.Age(binding);
        await Assert.That(declaration.EvaluateMetadata(binding, provenance, IndividualEvidenceData.Now).Kind)
            .IsEqualTo(ModerationAgeEvidenceKind.Unknown);
        await Assert.That(() => ModerationAgeEvidence.Record(Guid.CreateVersion7(), binding, 18,
            ModerationAgeEvidenceKind.Declared, ModerationDeclaredAgeOutcome.AtLeastThreshold,
            provenance, IndividualEvidenceData.Now, IndividualEvidenceData.End)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(ModerationSubjectKind.Service)]
    [Arguments(ModerationSubjectKind.Organization)]
    [Arguments(ModerationSubjectKind.Content)]
    public async Task Nonhuman_native_subjects_cannot_be_presented_as_individuals(ModerationSubjectKind kind)
    {
        var subject = SubjectReference.Create(kind, Guid.CreateVersion7(), IndividualEvidenceData.Instance);
        await Assert.That(() => ModerationIndividualTarget.Human(subject)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Assured_under_threshold_is_not_converted_to_adulthood_or_a_disciplinary_state()
    {
        var binding = IndividualEvidenceData.Binding();
        var provenance = IndividualEvidenceData.Assurance(binding);
        var evidence = ModerationAgeEvidence.Record(Guid.CreateVersion7(), binding, 18,
            ModerationAgeEvidenceKind.Assured, ModerationDeclaredAgeOutcome.UnderThreshold,
            provenance, IndividualEvidenceData.Now, IndividualEvidenceData.End);
        var metadata = evidence.EvaluateMetadata(binding, provenance, IndividualEvidenceData.Now);
        await Assert.That(metadata.Kind).IsEqualTo(ModerationAgeEvidenceKind.Assured);
        await Assert.That(metadata.Outcome).IsEqualTo(ModerationDeclaredAgeOutcome.UnderThreshold);
        await Assert.That(metadata.MinimumAge).IsEqualTo(18);
        await Assert.That(evidence.State).IsEqualTo(ModerationIndividualRecordState.Current);
        await Assert.That(evidence.Revision).IsEqualTo(1);
    }

    [Test]
    public async Task Manual_provenance_cannot_cross_subjects_or_outlive_qualification()
    {
        var binding = IndividualEvidenceData.Binding();
        var provenance = IndividualEvidenceData.Assurance(IndividualEvidenceData.Change(binding, "subject"));
        await Assert.That(() => IndividualEvidenceData.Age(binding, provenance)).Throws<ArgumentException>();
        var shortQualification = new ModerationAgeAssuranceProvenance(binding,
            ModerationAgeAssuranceMethod.LocalQualifiedManual, IndividualEvidenceData.Human(),
            IndividualEvidenceData.Human(), IndividualEvidenceData.Human(), IndividualEvidenceData.Human(),
            IndividualEvidenceData.Human(), IndividualEvidenceData.Reference(),
            IndividualEvidenceData.Reference(IndividualEvidenceData.Now.AddMinutes(2)));
        await Assert.That(() => IndividualEvidenceData.Age(binding, shortQualification)).Throws<ArgumentException>();
    }

    [Test]
    public async Task A_human_can_control_their_own_account_but_cannot_qualify_their_own_assertion()
    {
        var binding = IndividualEvidenceData.Binding();
        var issuer = IndividualEvidenceData.Human();
        var qualifier = IndividualEvidenceData.Human();
        var provenance = new ModerationAgeAssuranceProvenance(binding,
            ModerationAgeAssuranceMethod.LocalQualifiedManual, issuer, issuer, qualifier, qualifier,
            binding.Target.HumanSubject, IndividualEvidenceData.Reference(), IndividualEvidenceData.Reference());
        var evidence = IndividualEvidenceData.Age(binding, provenance);
        await Assert.That(evidence.EvaluateMetadata(binding, provenance, IndividualEvidenceData.Now).Kind)
            .IsEqualTo(ModerationAgeEvidenceKind.Assured);
    }
}

internal static class IndividualEvidenceData
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset End = Now.AddMinutes(10);
    internal static readonly AuthorityScope Instance = AuthorityScope.Instance(
        AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
        Guid.CreateVersion7());

    internal static SubjectReference Human(Guid? id = null) =>
        SubjectReference.Create(ModerationSubjectKind.Human, id ?? Guid.CreateVersion7(), Instance);

    internal static ModerationIndividualNativeReference Reference(DateTimeOffset? until = null) =>
        new(Guid.CreateVersion7(), 1, 1, Now, until ?? End);

    internal static ModerationIndividualBinding Binding(ModerationIndividualTarget? target = null) =>
        new(target ?? ModerationIndividualTarget.Human(Human()),
            new ModerationAgePolicy(Guid.CreateVersion7(), 1, Instance),
            ModerationIndividualPurpose.ProfileCompletion, Guid.CreateVersion7(), 1, 1,
            Guid.NewGuid(), 1, Now, End);

    internal static ModerationIndividualBinding Change(
        ModerationIndividualBinding value, string? change = null, ModerationIndividualTarget? target = null) =>
        new(target ?? (change == "subject" ? ModerationIndividualTarget.Human(Human()) : value.Target),
            new ModerationAgePolicy(change == "policy" ? Guid.CreateVersion7() : value.PolicyId,
                change == "revision" ? value.PolicyRevision + 1 : value.PolicyRevision,
                change == "jurisdiction" ? AuthorityScope.Tenant(Instance, Guid.CreateVersion7()) : value.Jurisdiction),
            change == "purpose" ? ModerationIndividualPurpose.ParticipantCollection : value.Purpose,
            change == "source" ? Guid.CreateVersion7() : value.SourceId,
            change == "source-revision" ? value.SourceRevision + 1 : value.SourceRevision,
            change == "generation" ? value.SourceGeneration + 1 : value.SourceGeneration,
            change == "stamp" ? Guid.NewGuid() : value.ObservationStamp,
            change == "sequence" ? value.ObservationSequence + 1 : value.ObservationSequence,
            value.ObservedAt, value.ValidUntil);

    internal static ModerationAgeAssuranceProvenance Assurance(ModerationIndividualBinding binding) =>
        new(binding, ModerationAgeAssuranceMethod.LocalQualifiedManual,
            Human(), Human(), Human(), Human(), Human(), Reference(), Reference());

    internal static ModerationAgeEvidence Age(
        ModerationIndividualBinding binding, ModerationAgeAssuranceProvenance? provenance = null) =>
        ModerationAgeEvidence.Record(Guid.CreateVersion7(), binding, 18,
            provenance is null ? ModerationAgeEvidenceKind.Declared : ModerationAgeEvidenceKind.Assured,
            ModerationDeclaredAgeOutcome.AtLeastThreshold, provenance, Now, End);
}
