using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationCollectionLifecycleTests
{
    [Test]
    [Arguments(-1, false)]
    [Arguments(0, true)]
    [Arguments(9, true)]
    [Arguments(10, false)]
    public async Task Receipt_metadata_has_exact_half_open_validity(int minutes, bool expected)
    {
        var binding = IndividualEvidenceData.Binding();
        var assessment = Assessment(binding);
        var prerequisites = Prerequisites(binding);
        var receipt = Record(binding, assessment, prerequisites);
        await Assert.That(receipt.MatchesCurrentMetadata(binding, assessment, prerequisites,
            IndividualEvidenceData.Now.AddMinutes(minutes))).IsEqualTo(expected);
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
    public async Task Collection_cannot_reuse_another_individual_purpose_or_source_generation(string change)
    {
        var binding = IndividualEvidenceData.Binding();
        var assessment = Assessment(binding);
        var prerequisites = Prerequisites(binding);
        var receipt = Record(binding, assessment, prerequisites);
        await Assert.That(receipt.MatchesCurrentMetadata(IndividualEvidenceData.Change(binding, change),
            assessment, prerequisites, IndividualEvidenceData.Now)).IsFalse();
    }

    [Test]
    public async Task Consent_for_one_session_individual_is_not_companion_or_purchaser_consent()
    {
        var session = Guid.CreateVersion7();
        var individual = Guid.CreateVersion7();
        var binding = IndividualEvidenceData.Binding(ModerationIndividualTarget.RegistrationSession(
            IndividualEvidenceData.Instance, session, individual));
        var assessment = Assessment(binding);
        var prerequisites = Prerequisites(binding);
        var receipt = Record(binding, assessment, prerequisites);
        var companion = IndividualEvidenceData.Change(binding, target: ModerationIndividualTarget.RegistrationSession(
            IndividualEvidenceData.Instance, session, Guid.CreateVersion7()));
        var purchaser = IndividualEvidenceData.Change(binding, target:
            ModerationIndividualTarget.Human(IndividualEvidenceData.Human(individual)));
        await Assert.That(receipt.MatchesCurrentMetadata(companion, assessment, prerequisites,
            IndividualEvidenceData.Now)).IsFalse();
        await Assert.That(receipt.MatchesCurrentMetadata(purchaser, assessment, prerequisites,
            IndividualEvidenceData.Now)).IsFalse();
    }

    [Test]
    public async Task Required_guardian_consent_cannot_be_omitted_or_replaced_by_account_eligibility()
    {
        var binding = IndividualEvidenceData.Binding();
        var assessment = Assessment(binding);
        await Assert.That(() => Record(binding, assessment, [])).Throws<ArgumentException>();
        var account = new ModerationCollectionPrerequisite(binding,
            ModerationCollectionPrerequisiteKind.AccountEligibility, IndividualEvidenceData.Reference());
        await Assert.That(() => Record(binding, assessment, [account])).Throws<ArgumentException>();
    }

    [Test]
    public async Task Even_no_required_prerequisites_needs_a_native_bound_applicability_assessment()
    {
        var binding = IndividualEvidenceData.Binding();
        var assessment = new ModerationCollectionPrerequisiteAssessment(binding,
            IndividualEvidenceData.Reference(), []);
        var receipt = Record(binding, assessment, []);
        await Assert.That(receipt.MatchesCurrentMetadata(binding, assessment, [],
            IndividualEvidenceData.Now)).IsTrue();
        await Assert.That(() => Record(binding, null!, [])).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Prerequisite_and_applicability_observations_cannot_be_transplanted()
    {
        var binding = IndividualEvidenceData.Binding();
        var other = IndividualEvidenceData.Change(binding, "subject");
        await Assert.That(() => Record(binding, Assessment(other), Prerequisites(binding)))
            .Throws<ArgumentException>();
        await Assert.That(() => Record(binding, Assessment(binding), Prerequisites(other)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Replacement_or_withdrawal_of_prerequisites_invalidates_current_metadata()
    {
        var binding = IndividualEvidenceData.Binding();
        var assessment = Assessment(binding);
        var prerequisites = Prerequisites(binding);
        var receipt = Record(binding, assessment, prerequisites);
        await Assert.That(receipt.MatchesCurrentMetadata(binding, assessment, Prerequisites(binding),
            IndividualEvidenceData.Now)).IsFalse();
        await Assert.That(receipt.MatchesCurrentMetadata(binding, Assessment(binding), prerequisites,
            IndividualEvidenceData.Now)).IsFalse();
        await Assert.That(receipt.MatchesCurrentMetadata(binding, assessment, [],
            IndividualEvidenceData.Now)).IsFalse();
    }

    [Test]
    public async Task Caller_collection_mutation_cannot_remove_required_consent()
    {
        var binding = IndividualEvidenceData.Binding();
        var required = new[] { ModerationCollectionPrerequisiteKind.GuardianConsent };
        var assessment = new ModerationCollectionPrerequisiteAssessment(binding,
            IndividualEvidenceData.Reference(), required);
        var prerequisites = Prerequisites(binding);
        var receipt = Record(binding, assessment, prerequisites);
        required[0] = ModerationCollectionPrerequisiteKind.AccountEligibility;
        var original = prerequisites[0];
        prerequisites[0] = new ModerationCollectionPrerequisite(binding,
            ModerationCollectionPrerequisiteKind.AccountEligibility, IndividualEvidenceData.Reference());
        await Assert.That(receipt.MatchesCurrentMetadata(binding, assessment, [original],
            IndividualEvidenceData.Now)).IsTrue();
        await Assert.That(receipt.MatchesCurrentMetadata(binding, assessment, prerequisites,
            IndividualEvidenceData.Now)).IsFalse();
    }

    [Test]
    public async Task Duplicate_consent_and_default_requirement_discriminants_fail_closed()
    {
        var binding = IndividualEvidenceData.Binding();
        var guardian = Prerequisites(binding)[0];
        await Assert.That(() => Record(binding, Assessment(binding), [guardian, guardian]))
            .Throws<ArgumentException>();
        await Assert.That(() => new ModerationCollectionPrerequisiteAssessment(binding,
            IndividualEvidenceData.Reference(), [(ModerationCollectionPrerequisiteKind)0]))
            .Throws<ArgumentException>();
        await Assert.That(() => new ModerationCollectionPrerequisite(binding,
            (ModerationCollectionPrerequisiteKind)0, IndividualEvidenceData.Reference()))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(ModerationIndividualRecordState.Corrected)]
    [Arguments(ModerationIndividualRecordState.Withdrawn)]
    [Arguments(ModerationIndividualRecordState.Expired)]
    [Arguments(ModerationIndividualRecordState.Erased)]
    public async Task Retired_collection_metadata_cannot_be_reactivated_or_applied_retrospectively(
        ModerationIndividualRecordState state)
    {
        var binding = IndividualEvidenceData.Binding();
        var assessment = Assessment(binding);
        var prerequisites = Prerequisites(binding);
        var receipt = Record(binding, assessment, prerequisites);
        var now = state == ModerationIndividualRecordState.Expired
            ? IndividualEvidenceData.End : IndividualEvidenceData.Now.AddMinutes(1);
        receipt.Retire(1, state, now);
        await Assert.That(receipt.MatchesCurrentMetadata(binding, assessment, prerequisites,
            IndividualEvidenceData.Now)).IsFalse();
        await Assert.That(receipt.Binding).IsNull();
        await Assert.That(receipt.Assessment).IsNull();
        await Assert.That(receipt.Prerequisites.Count).IsEqualTo(0);
        await Assert.That(receipt.ValidUntil).IsEqualTo(IndividualEvidenceData.End);
        await Assert.That(() => receipt.Retire(2, ModerationIndividualRecordState.Current, now))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Retirement_requires_current_revision_and_monotonic_time_but_erasure_survives_expiry()
    {
        var binding = IndividualEvidenceData.Binding();
        var receipt = Record(binding, Assessment(binding), Prerequisites(binding));
        await Assert.That(() => receipt.Retire(2, ModerationIndividualRecordState.Withdrawn,
            IndividualEvidenceData.Now)).Throws<InvalidOperationException>();
        await Assert.That(() => receipt.Retire(1, ModerationIndividualRecordState.Withdrawn,
            IndividualEvidenceData.Now.AddTicks(-1))).Throws<InvalidOperationException>();
        await Assert.That(() => receipt.Retire(1, (ModerationIndividualRecordState)0,
            IndividualEvidenceData.Now)).Throws<InvalidOperationException>();
        receipt.Retire(1, ModerationIndividualRecordState.Expired, IndividualEvidenceData.End);
        receipt.Retire(2, ModerationIndividualRecordState.Erased, IndividualEvidenceData.End.AddMinutes(1));
        await Assert.That(receipt.State).IsEqualTo(ModerationIndividualRecordState.Erased);
    }

    [Test]
    public async Task Receipt_cannot_extend_any_native_validity_ceiling()
    {
        var binding = IndividualEvidenceData.Binding();
        var assessment = Assessment(binding);
        var shortConsent = new ModerationCollectionPrerequisite(binding,
            ModerationCollectionPrerequisiteKind.GuardianConsent,
            IndividualEvidenceData.Reference(IndividualEvidenceData.Now.AddMinutes(2)));
        await Assert.That(() => Record(binding, assessment, [shortConsent])).Throws<ArgumentException>();
        await Assert.That(() => ModerationCollectionReceipt.Record(Guid.CreateVersion7(), binding,
            assessment, Prerequisites(binding), IndividualEvidenceData.Now,
            IndividualEvidenceData.End.AddTicks(1))).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("empty")]
    [Arguments("infinite")]
    [Arguments("offset")]
    [Arguments("default-purpose")]
    [Arguments("zero-sequence")]
    [Arguments("empty-stamp")]
    public async Task Invalid_observation_windows_and_discriminants_cannot_be_bound(string change)
    {
        var value = IndividualEvidenceData.Binding();
        await Assert.That(() => new ModerationIndividualBinding(value.Target,
            new ModerationAgePolicy(value.PolicyId, value.PolicyRevision, value.Jurisdiction),
            change == "default-purpose" ? (ModerationIndividualPurpose)0 : value.Purpose,
            value.SourceId, value.SourceRevision, value.SourceGeneration,
            change == "empty-stamp" ? Guid.Empty : value.ObservationStamp,
            change == "zero-sequence" ? 0 : value.ObservationSequence,
            change == "offset" ? value.ObservedAt.ToOffset(TimeSpan.FromHours(1)) : value.ObservedAt,
            change == "empty" ? value.ObservedAt : change == "infinite" ? DateTimeOffset.MaxValue : value.ValidUntil))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Last_tick_is_current_but_exact_expiry_never_extends_collection()
    {
        var binding = IndividualEvidenceData.Binding();
        var assessment = Assessment(binding);
        var prerequisites = Prerequisites(binding);
        var receipt = Record(binding, assessment, prerequisites);
        await Assert.That(receipt.MatchesCurrentMetadata(binding, assessment, prerequisites,
            IndividualEvidenceData.End.AddTicks(-1))).IsTrue();
        await Assert.That(receipt.MatchesCurrentMetadata(binding, assessment, prerequisites,
            IndividualEvidenceData.End)).IsFalse();
    }

    [Test]
    public async Task All_assessed_prerequisites_are_required_independent_of_enumeration_order()
    {
        var binding = IndividualEvidenceData.Binding();
        var kinds = new[] { ModerationCollectionPrerequisiteKind.GuardianConsent,
            ModerationCollectionPrerequisiteKind.AccountEligibility, ModerationCollectionPrerequisiteKind.SubjectConsent };
        var assessment = new ModerationCollectionPrerequisiteAssessment(binding, IndividualEvidenceData.Reference(), kinds);
        var prerequisites = kinds.Select(kind =>
            new ModerationCollectionPrerequisite(binding, kind, IndividualEvidenceData.Reference())).ToArray();
        var receipt = Record(binding, assessment, prerequisites);
        await Assert.That(receipt.MatchesCurrentMetadata(binding, assessment, prerequisites.Reverse(),
            IndividualEvidenceData.Now)).IsTrue();
        await Assert.That(() => Record(binding, assessment, prerequisites.Take(2))).Throws<ArgumentException>();
    }

    [Test]
    public async Task One_native_record_cannot_masquerade_as_both_subject_and_guardian_consent()
    {
        var binding = IndividualEvidenceData.Binding();
        var assessment = new ModerationCollectionPrerequisiteAssessment(binding, IndividualEvidenceData.Reference(),
            [ModerationCollectionPrerequisiteKind.SubjectConsent, ModerationCollectionPrerequisiteKind.GuardianConsent]);
        var reference = IndividualEvidenceData.Reference();
        await Assert.That(() => Record(binding, assessment,
            [new(binding, ModerationCollectionPrerequisiteKind.SubjectConsent, reference),
                new(binding, ModerationCollectionPrerequisiteKind.GuardianConsent, reference)]))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Audience_threshold_metadata_is_not_a_sensitive_collection_purpose()
    {
        var original = IndividualEvidenceData.Binding();
        var binding = new ModerationIndividualBinding(original.Target,
            new ModerationAgePolicy(original.PolicyId, original.PolicyRevision, original.Jurisdiction),
            ModerationIndividualPurpose.AudienceThreshold, original.SourceId, original.SourceRevision,
            original.SourceGeneration, original.ObservationStamp, original.ObservationSequence,
            original.ObservedAt, original.ValidUntil);
        await Assert.That(() => Record(binding, Assessment(binding), Prerequisites(binding))).Throws<ArgumentException>();
    }

    private static ModerationCollectionPrerequisiteAssessment Assessment(ModerationIndividualBinding binding) =>
        new(binding, IndividualEvidenceData.Reference(), [ModerationCollectionPrerequisiteKind.GuardianConsent]);

    private static ModerationCollectionPrerequisite[] Prerequisites(ModerationIndividualBinding binding) =>
        [new(binding, ModerationCollectionPrerequisiteKind.GuardianConsent, IndividualEvidenceData.Reference())];

    private static ModerationCollectionReceipt Record(ModerationIndividualBinding binding,
        ModerationCollectionPrerequisiteAssessment assessment, IEnumerable<ModerationCollectionPrerequisite> prerequisites) =>
        ModerationCollectionReceipt.Record(Guid.CreateVersion7(), binding, assessment, prerequisites,
            IndividualEvidenceData.Now, IndividualEvidenceData.End);
}
