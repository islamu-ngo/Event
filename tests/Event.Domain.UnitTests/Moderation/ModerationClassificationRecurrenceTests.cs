using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationClassificationRecurrenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(0, 0)]
    [Arguments(1, 30)]
    [Arguments(2, 31)]
    [Arguments(3, 32)]
    [Arguments(4, 33)]
    [Arguments(8, 33)]
    public async Task Distinct_accepted_occurrences_select_only_the_reference_publication_stage(int count, int profile)
    {
        var fixture = new Fixture();
        var history = Enumerable.Range(0, count).Select(index => fixture.Entry(Now.AddDays(-index - 1))).ToArray();

        var result = fixture.Assess(history);

        await Assert.That(result.QualifyingOccurrenceCount).IsEqualTo(count);
        await Assert.That(result.ReferenceProfileId).IsEqualTo(profile == 0 ? null : (ModerationReferenceProfileId?)profile);
        await Assert.That(result.Contributions.Count).IsEqualTo(count);
        await Assert.That(result.Subject).IsEqualTo(fixture.Subject);
        await Assert.That(result.Scope).IsEqualTo(fixture.Scope);
    }

    [Test]
    public async Task Multiple_findings_and_attribution_generations_from_one_occurrence_count_once()
    {
        var fixture = new Fixture();
        var occurrence = Guid.CreateVersion7();
        var first = fixture.Entry(Now.AddDays(-1), occurrence: occurrence, generation: 1);
        var second = fixture.Entry(Now.AddDays(-1), occurrence: occurrence, generation: 2);

        var result = fixture.Assess([second, first, first]);

        await Assert.That(result.QualifyingOccurrenceCount).IsEqualTo(1);
        await Assert.That(result.ReferenceProfileId).IsEqualTo(ModerationReferenceProfileId.CFG30);
        await Assert.That(result.Contributions.Count).IsEqualTo(2);
        await Assert.That(result.Contributions.All(cause => cause.OccurrenceId == occurrence)).IsTrue();
        await Assert.That(result.Contributions.Select(cause => cause.FindingId).ToHashSet()
            .SetEquals([first.FindingId, second.FindingId])).IsTrue();
        await Assert.That(result.Contributions.Select(cause => cause.AttributionGeneration).ToHashSet()
            .SetEquals([1L, 2L])).IsTrue();
    }

    [Test]
    public async Task Reversal_recomputation_removes_only_that_findings_contribution()
    {
        var fixture = new Fixture();
        var first = fixture.Finding(Now.AddDays(-2));
        var second = fixture.Entry(Now.AddDays(-1));
        var before = fixture.Assess([fixture.Capture(first, Now.AddDays(-2)), second]);
        first.Reverse(Guid.CreateVersion7(), first.Adjudicator, "Independent correction of the classification finding.", 1, Now);

        var after = fixture.Assess([fixture.Capture(first, Now.AddDays(-2)), second]);

        await Assert.That(before.ReferenceProfileId).IsEqualTo(ModerationReferenceProfileId.CFG31);
        await Assert.That(before.Contributions.Count).IsEqualTo(2);
        await Assert.That(after.ReferenceProfileId).IsEqualTo(ModerationReferenceProfileId.CFG30);
        await Assert.That(after.Contributions.Single().FindingId).IsEqualTo(second.FindingId);
    }

    [Test]
    public async Task Reversing_one_duplicate_does_not_erase_an_independent_final_finding_of_that_occurrence()
    {
        var fixture = new Fixture();
        var first = fixture.Finding(Now.AddDays(-1));
        var second = fixture.Entry(Now.AddDays(-1), occurrence: first.CaseOccurrenceId);
        first.Reverse(Guid.CreateVersion7(), first.Adjudicator, "This finding alone was reversed.", 1, Now);

        var result = fixture.Assess([fixture.Capture(first, Now.AddDays(-1)), second]);

        await Assert.That(result.QualifyingOccurrenceCount).IsEqualTo(1);
        await Assert.That(result.Contributions.Single().FindingId).IsEqualTo(second.FindingId);
    }

    [Test]
    [Arguments("missing")]
    [Arguments("later-acceptance")]
    [Arguments("different-rule")]
    [Arguments("different-revision")]
    [Arguments("different-subject")]
    [Arguments("different-jurisdiction")]
    public async Task Unaccepted_or_mismatched_policy_history_cannot_create_a_strike(string defect)
    {
        var fixture = new Fixture();
        var occurredAt = Now.AddDays(-1);
        var finding = fixture.Finding(occurredAt);
        var acceptance = defect == "missing" ? null : ModerationClassificationPolicyAcceptance.Record(
            Guid.CreateVersion7(),
            defect == "different-subject" ? SubjectReference.Create(fixture.Subject.Kind, Guid.CreateVersion7(), fixture.Scope) : fixture.Subject,
            defect == "different-jurisdiction" ? AuthorityScope.Tenant(fixture.Scope.Parent!, Guid.CreateVersion7()) : fixture.Scope,
            defect == "different-rule" ? ModerationRuleReference.Create(fixture.Scope.Id, "honest-correction", 1)
                : defect == "different-revision" ? ModerationRuleReference.Create(fixture.Scope.Id, fixture.Rule.Code, 2) : fixture.Rule,
            defect == "later-acceptance" ? occurredAt.AddTicks(1) : occurredAt);

        var result = fixture.Assess([ModerationClassificationFinding.Capture(finding, occurredAt, acceptance)]);

        await Assert.That(result.QualifyingOccurrenceCount).IsEqualTo(0);
        await Assert.That(result.ReferenceProfileId).IsNull();
    }

    [Test]
    public async Task A_new_policy_does_not_reinterpret_a_finding_of_the_actually_accepted_old_rule()
    {
        var fixture = new Fixture();
        var old = fixture.Entry(Now.AddDays(-2));
        var revised = ModerationRuleReference.Create(fixture.Scope.Id, fixture.Rule.Code, 2);
        var current = fixture.Finding(Now.AddDays(-1), rule: revised);
        var acceptance = ModerationClassificationPolicyAcceptance.Record(Guid.CreateVersion7(), fixture.Subject,
            fixture.Scope, revised, Now.AddDays(-1));

        var result = fixture.Assess([old, ModerationClassificationFinding.Capture(current, Now.AddDays(-1), acceptance)]);

        await Assert.That(result.QualifyingOccurrenceCount).IsEqualTo(2);
        await Assert.That(result.Contributions.Select(cause => cause.Rule.Revision).ToHashSet().SetEquals([1L, 2L])).IsTrue();
        await Assert.That(result.Contributions.Any(cause => cause.PolicyAcceptanceId == acceptance.Id)).IsTrue();
    }

    [Test]
    public async Task Window_uses_original_conduct_time_not_delayed_finalization_or_replay_time()
    {
        var fixture = new Fixture();
        var outside = fixture.Finding(Now.AddDays(-180));
        var expired = fixture.Capture(outside, Now.AddDays(-180));
        var live = fixture.Entry(Now.AddDays(-180).AddTicks(1));
        var future = fixture.Entry(Now.AddTicks(1));

        var result = fixture.Assess([expired, live, future]);

        await Assert.That(result.WindowStartExclusive).IsEqualTo(Now.AddDays(-180));
        await Assert.That(result.QualifyingOccurrenceCount).IsEqualTo(1);
        await Assert.That(result.Contributions.Single().FindingId).IsEqualTo(live.FindingId);
        await Assert.That(fixture.Assess([live], Now.AddTicks(1)).QualifyingOccurrenceCount).IsEqualTo(0);
    }

    [Test]
    [Arguments("subject")]
    [Arguments("subject-origin")]
    [Arguments("subject-kind")]
    [Arguments("tenant")]
    [Arguments("trust")]
    public async Task Same_looking_ids_cannot_merge_canonical_subjects_or_jurisdictions(string defect)
    {
        var fixture = new Fixture();
        var foreignScope = defect == "trust" ? AuthorityScope.Tenant(AuthorityScope.Instance(AuthorityScope.Solution(
                AuthorityScope.TrustDomain(Guid.CreateVersion7()), fixture.Scope.Parent!.Parent!.Id),
                fixture.Scope.Parent.Id), fixture.Scope.Id)
            : AuthorityScope.Tenant(fixture.Scope.Parent!, Guid.CreateVersion7());
        var scope = defect is "trust" or "tenant" ? foreignScope : fixture.Scope;
        var subject = SubjectReference.Create(defect == "subject-kind" ? ModerationSubjectKind.Service : fixture.Subject.Kind,
            defect == "subject" ? Guid.CreateVersion7() : fixture.Subject.Id,
            defect == "subject-origin" ? foreignScope : scope);
        var foreign = new Fixture(scope, subject).Entry(Now.AddDays(-1));

        await Assert.That(fixture.Assess([foreign]).QualifyingOccurrenceCount).IsEqualTo(0);
    }

    [Test]
    public async Task Conflicting_snapshots_of_one_finding_fail_closed_instead_of_preferring_a_stale_final_state()
    {
        var fixture = new Fixture();
        var finding = fixture.Finding(Now.AddDays(-1));
        var stale = fixture.Capture(finding, Now.AddDays(-1));
        finding.Reverse(Guid.CreateVersion7(), finding.Adjudicator, "A current reversal cannot be hidden by a duplicate.", 1, Now);
        var current = ModerationClassificationFinding.Capture(finding, Now.AddDays(-1), stale.Acceptance);

        await Assert.That(() => fixture.Assess([stale, current])).Throws<ArgumentException>();
    }

    [Test]
    public async Task Result_owns_ordered_provenance_and_does_not_follow_later_entity_or_input_mutation()
    {
        var fixture = new Fixture();
        var finding = fixture.Finding(Now.AddDays(-1));
        var earlier = fixture.Entry(Now.AddDays(-2));
        var history = new List<ModerationClassificationFinding> { fixture.Capture(finding, Now.AddDays(-1)), earlier };
        var result = fixture.Assess(history);
        history.Clear();
        finding.Reverse(Guid.CreateVersion7(), finding.Adjudicator, "Later correction requires a fresh assessment.", 1, Now);

        await Assert.That(result.Contributions[0].FindingId).IsEqualTo(earlier.FindingId);
        await Assert.That(result.Contributions[1].FindingRevision).IsEqualTo(1L);
        await Assert.That(result.QualifyingOccurrenceCount).IsEqualTo(2);
        await Assert.That(() => ((IList<ModerationClassificationContribution>)result.Contributions).Clear())
            .Throws<NotSupportedException>();
    }

    [Test]
    public async Task Occurrence_time_cannot_be_fabricated_after_the_original_case_was_opened()
    {
        var fixture = new Fixture();
        var finding = fixture.Finding(Now.AddDays(-1));

        await Assert.That(() => fixture.Capture(finding, finding.CaseCreatedAt.AddTicks(1))).Throws<ArgumentException>();
    }

    [Test]
    public async Task Multiple_findings_cannot_refresh_the_same_occurrences_original_time()
    {
        var fixture = new Fixture();
        var first = fixture.Entry(Now.AddDays(-2));
        var refreshed = fixture.Entry(Now.AddDays(-1), occurrence: first.OccurrenceId);

        await Assert.That(() => fixture.Assess([first, refreshed])).Throws<ArgumentException>();
    }

    [Test]
    public async Task One_acceptance_identity_cannot_claim_two_policy_versions()
    {
        var fixture = new Fixture();
        var first = fixture.Entry(Now.AddDays(-2));
        var revised = ModerationRuleReference.Create(fixture.Scope.Id, fixture.Rule.Code, 2);
        var finding = fixture.Finding(Now.AddDays(-1), rule: revised);
        var forged = ModerationClassificationPolicyAcceptance.Record(first.Acceptance!.Id, fixture.Subject,
            fixture.Scope, revised, first.Acceptance.AcceptedAt);
        var second = ModerationClassificationFinding.Capture(finding, Now.AddDays(-1), forged);

        await Assert.That(() => fixture.Assess([first, second])).Throws<ArgumentException>();
    }

    [Test]
    public async Task A_finding_finalized_after_assessment_cannot_be_counted_even_for_older_conduct()
    {
        var fixture = new Fixture();
        var entry = fixture.Entry(Now.AddDays(-2));

        await Assert.That(fixture.Assess([entry], Now.AddTicks(-1)).QualifyingOccurrenceCount).IsEqualTo(0);
    }

    private sealed class Fixture
    {
        public Fixture(AuthorityScope? scope = null, SubjectReference? subject = null)
        {
            Scope = scope ?? AuthorityScope.Tenant(AuthorityScope.Instance(AuthorityScope.Solution(
                AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
            Subject = subject ?? SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), Scope);
            Rule = ModerationRuleReference.Create(Scope.Id, "accepted-classification-violation", 1);
        }

        public AuthorityScope Scope { get; }
        public SubjectReference Subject { get; }
        public ModerationRuleReference Rule { get; }

        public ModerationClassificationFinding Entry(DateTimeOffset occurredAt, Guid? occurrence = null, long? generation = null) =>
            Capture(Finding(occurredAt, occurrence, generation), occurredAt);

        public ModerationClassificationFinding Capture(ModerationFinding finding, DateTimeOffset occurredAt) =>
            ModerationClassificationFinding.Capture(finding, occurredAt, ModerationClassificationPolicyAcceptance.Record(
                Guid.CreateVersion7(), Subject, Scope, finding.Rule, occurredAt));

        public ModerationClassificationAssessment Assess(IEnumerable<ModerationClassificationFinding> history, DateTimeOffset? now = null) =>
            ModerationClassificationRecurrence.Assess(Subject, Scope, history, now ?? Now);

        public ModerationFinding Finding(DateTimeOffset occurredAt, Guid? occurrence = null, long? generation = null,
            ModerationRuleReference? rule = null)
        {
            var owner = ModerationCase.Open(Guid.CreateVersion7(), Subject, Scope, occurrence ?? Guid.CreateVersion7(), occurredAt);
            if (generation is not null)
            {
                var evidence = ModerationEvidenceReference.Create(ModerationCaseTarget.Create(
                    ModerationCaseTargetKind.EventVersion, Guid.CreateVersion7(), Scope, Guid.CreateVersion7()), Scope,
                    owner.OccurrenceId, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
                var allegation = ModerationCase.OpenAllegation(Guid.CreateVersion7(), Scope, evidence, occurredAt);
                owner = ModerationCase.OpenAttributed(Guid.CreateVersion7(), Subject, allegation, 1, Guid.CreateVersion7(), occurredAt);
            }
            var reviewer = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), Scope.Parent!);
            var selectedRule = rule ?? Rule;
            return ModerationFinding.Finalize(Guid.CreateVersion7(), owner, 1, Scope, owner.AttributionSourceId,
                generation, selectedRule, selectedRule, selectedRule, selectedRule, reviewer,
                ModerationFindingEvidenceReference.Response(Guid.CreateVersion7(), owner.Id, 1, occurredAt),
                "An independently qualified final accepted-policy classification violation.", occurredAt > Now ? occurredAt : Now);
        }
    }
}
