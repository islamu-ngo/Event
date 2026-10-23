using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationScheduleRiskReviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ResourceUuidCannotSubstituteForTypedScheduleEvent()
    {
        var (_, _, coordinates, _, _) = Create();
        var resource = AuthorityScope.Resource(coordinates.EventScope.Parent!, coordinates.EventScope.Id);
        await Assert.That(() => ModerationScheduleRiskCoordinates.Create(resource, coordinates.SessionId,
            coordinates.ScheduleGeneration, coordinates.ParticipantId, coordinates.Participant,
            coordinates.ParticipantGeneration, coordinates.PolicyId, coordinates.PolicyRevision,
            coordinates.PublicationId, coordinates.Policy)).Throws<ArgumentException>();
    }

    [Test]
    public async Task ScheduleReviewHydrationPreservesTypedEventAndBothEventOriginOwners()
    {
        var (_, sanction, coordinates, _, _) = Create();
        var owner = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), coordinates.EventScope);
        var escalation = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), coordinates.EventScope);
        var review = ModerationScheduleRiskReview.Open(Guid.CreateVersion7(), sanction, 1, coordinates,
            owner, escalation, Now.AddHours(3), Now);
        var restored = (ModerationScheduleRiskReview)Activator.CreateInstance(typeof(ModerationScheduleRiskReview),
            nonPublic: true)!;
        foreach (var property in typeof(ModerationScheduleRiskReview).GetProperties()
            .Where(property => property.SetMethod is { IsPrivate: true }
                && (property.PropertyType.IsValueType || property.PropertyType == typeof(string))))
            property.SetValue(restored, property.GetValue(review));

        restored.ValidatePersistedState();
        await Assert.That(restored.EventScope).IsEqualTo(coordinates.EventScope);
        await Assert.That(restored.OriginalCoordinates).IsEqualTo(coordinates);
        await Assert.That(restored.Coordinates).IsEqualTo(coordinates);
        await Assert.That(restored.Owner).IsEqualTo(owner);
        await Assert.That(restored.EscalationOwner).IsEqualTo(escalation);
        await Assert.That(restored.EventScopeResourceId).IsNull();
        await Assert.That(restored.EventScopeEventId).IsEqualTo(coordinates.EventScope.Id);
    }

    [Test]
    public async Task Separate_owned_review_preserves_original_sanction_even_after_its_end()
    {
        var (review, sanction, coordinates, _, _) = Create();
        var originalRevision = sanction.Revision;
        review.Reconcile(1, Now.AddHours(3));
        await Assert.That(review.State).IsEqualTo(ModerationScheduleRiskReviewState.Overdue);
        await Assert.That(review.OriginalSanctionEndsAt).IsEqualTo(Now.AddHours(1));
        await Assert.That(review.SourceSanctionId).IsEqualTo(sanction.Id);
        await Assert.That(review.SourceDecisionId).IsEqualTo(sanction.DecisionId);
        await Assert.That(review.SourceRule).IsEqualTo(sanction.Rule);
        await Assert.That(review.Coordinates).IsEqualTo(coordinates);
        await Assert.That(sanction.EndsAt).IsEqualTo(Now.AddHours(1));
        await Assert.That(sanction.Revision).IsEqualTo(originalRevision);
        await Assert.That(sanction.IsEffectiveAt(Now.AddHours(3))).IsFalse();
    }

    [Test]
    public async Task Exact_review_records_structural_acceptance_not_a_new_sanction()
    {
        var (review, sanction, coordinates, finding, reviewer) = Create();
        var intent = Guid.CreateVersion7();
        await Assert.That(review.Accept(intent, finding, reviewer, reviewer.Revision, coordinates, 1, Now)).IsTrue();
        await Assert.That(review.State).IsEqualTo(ModerationScheduleRiskReviewState.Accepted);
        await Assert.That(review.AcceptedFindingId).IsEqualTo(finding.Id);
        await Assert.That(review.AcceptedReviewerAssignmentId).IsEqualTo(reviewer.Id);
        await Assert.That(review.Revision).IsEqualTo(2L);
        await Assert.That(review.Accept(intent, finding, reviewer, reviewer.Revision, coordinates, 2, Now)).IsFalse();
        await Assert.That(review.Revision).IsEqualTo(2L);
        await Assert.That(sanction.Revision).IsEqualTo(1L);
        await Assert.That(review.Reconcile(2, Now.AddHours(4))).IsFalse();
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
    public async Task Stale_schedule_participant_policy_or_event_cannot_accept_review(int change)
    {
        var (review, _, coordinates, finding, reviewer) = Create();
        var current = Coordinates(coordinates, change);
        await Assert.That(() => review.Accept(Guid.CreateVersion7(), finding, reviewer,
            reviewer.Revision, current, 1, Now)).Throws<InvalidOperationException>();
        await Assert.That(review.State).IsEqualTo(ModerationScheduleRiskReviewState.Pending);
        await Assert.That(review.Revision).IsEqualTo(1L);
        await Assert.That(review.AcceptedFindingId).IsNull();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    public async Task Conflicted_expired_withdrawn_or_stale_review_authority_cannot_accept(int change)
    {
        var (review, _, coordinates, finding, reviewer) = Create();
        var observed = Now;
        var expectedReviewer = reviewer.Revision;
        if (change == 0)
        {
            reviewer.RecordConflictAssessment(reviewer.Revision, ModerationReviewerConflictState.Conflicted,
                Guid.CreateVersion7(), Guid.CreateVersion7(), Now);
            expectedReviewer = reviewer.Revision;
        }
        if (change == 1) observed = Now.AddMinutes(30);
        if (change == 2)
        {
            reviewer.Withdraw(reviewer.Revision, Guid.CreateVersion7(), Now);
            expectedReviewer = reviewer.Revision;
        }
        if (change == 3) expectedReviewer++;
        if (change == 4)
            finding.Reverse(Guid.CreateVersion7(), finding.Adjudicator, "Independent correction.", 1, Now);
        if (change == 5) finding = Create().Finding;
        await Assert.That(() => review.Accept(Guid.CreateVersion7(), finding, reviewer,
            expectedReviewer, coordinates, 1, observed)).Throws<InvalidOperationException>();
        await Assert.That(review.Revision).IsEqualTo(1L);
        await Assert.That(review.State).IsEqualTo(ModerationScheduleRiskReviewState.Pending);
    }

    [Test]
    public async Task Exact_deadline_is_overdue_without_waiting_for_reconciliation()
    {
        var (review, _, coordinates, finding, reviewer) = Create();
        var due = review.DueAt;
        await Assert.That(review.IsOverdueAt(due.AddTicks(-1))).IsFalse();
        await Assert.That(review.IsOverdueAt(due)).IsTrue();
        await Assert.That(() => review.Accept(Guid.CreateVersion7(), finding, reviewer,
            reviewer.Revision, coordinates, 1, due)).Throws<InvalidOperationException>();
        await Assert.That(review.Reconcile(1, due)).IsTrue();
        await Assert.That(review.Reconcile(2, due)).IsFalse();
        await Assert.That(review.State).IsEqualTo(ModerationScheduleRiskReviewState.Overdue);
        await Assert.That(review.EscalationOwner).IsEqualTo(review.Owner);
    }

    [Test]
    public async Task Revision_rebind_invalidates_old_coordinates_and_keeps_original_provenance()
    {
        var (review, sanction, coordinates, finding, reviewer) = Create();
        var replacement = Coordinates(coordinates, 0);
        review.Rebind(replacement, Now.AddHours(4), 1, Now);
        await Assert.That(review.OriginalCoordinates).IsEqualTo(coordinates);
        await Assert.That(review.Coordinates).IsEqualTo(replacement);
        await Assert.That(review.OriginalDueAt).IsEqualTo(Now.AddHours(3));
        await Assert.That(review.DueAt).IsEqualTo(Now.AddHours(4));
        await Assert.That(review.OriginalSanctionEndsAt).IsEqualTo(sanction.EndsAt);
        await Assert.That(() => review.Accept(Guid.CreateVersion7(), finding, reviewer,
            reviewer.Revision, coordinates, 2, Now)).Throws<InvalidOperationException>();
        await Assert.That(review.Accept(Guid.CreateVersion7(), finding, reviewer,
            reviewer.Revision, replacement, 2, Now)).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task Rebinding_cannot_restart_overdue_work_regress_revisions_or_change_ownership(int change)
    {
        var (review, _, coordinates, _, _) = Create();
        var replacement = change == 1 ? coordinates : Coordinates(coordinates, change == 2 ? 3 : change == 4 ? 5 : 0);
        var observed = change == 0 ? review.DueAt : Now;
        var expected = change == 3 ? 2 : 1;
        await Assert.That(() => review.Rebind(replacement, Now.AddHours(5), expected, observed))
            .Throws<InvalidOperationException>();
        await Assert.That(review.Revision).IsEqualTo(1L);
        await Assert.That(review.Coordinates).IsEqualTo(coordinates);
    }

    [Test]
    public async Task Withdrawn_work_is_terminal_and_exact_intent_replay_does_not_mutate_it()
    {
        var (review, _, coordinates, finding, reviewer) = Create();
        var intent = Guid.CreateVersion7();
        await Assert.That(review.Withdraw(intent, review.Owner, 1, Now)).IsTrue();
        await Assert.That(review.Withdraw(intent, review.Owner, 2, Now)).IsFalse();
        await Assert.That(review.State).IsEqualTo(ModerationScheduleRiskReviewState.Withdrawn);
        await Assert.That(() => review.Accept(Guid.CreateVersion7(), finding, reviewer,
            reviewer.Revision, coordinates, 2, Now)).Throws<InvalidOperationException>();
        await Assert.That(() => review.Rebind(Coordinates(coordinates, 0), Now.AddHours(5), 2, Now))
            .Throws<InvalidOperationException>();
        await Assert.That(review.Reconcile(2, Now.AddHours(5))).IsFalse();
        await Assert.That(review.Revision).IsEqualTo(2L);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task Review_requires_explicit_finite_deadline_and_accountable_same_trust_owners(int change)
    {
        var (_, sanction, coordinates, _, _) = Create();
        var owner = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), sanction.Scope);
        var escalation = change == 3
            ? SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(),
                AuthorityScope.TrustDomain(Guid.CreateVersion7()))
            : owner;
        var due = change switch
        {
            0 => Now,
            1 => DateTimeOffset.MaxValue,
            2 => Now.AddHours(3).ToOffset(TimeSpan.FromHours(1)),
            _ => Now.AddHours(3)
        };
        await Assert.That(() => ModerationScheduleRiskReview.Open(Guid.CreateVersion7(), sanction,
            sanction.Revision, coordinates, owner, escalation, due, Now)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Display_and_failure_surfaces_do_not_include_identity_coordinates()
    {
        var (review, _, coordinates, _, _) = Create();
        await Assert.That(review.ToString()).IsEqualTo(nameof(ModerationScheduleRiskReview));
        await Assert.That(coordinates.ToString()).IsEqualTo(nameof(ModerationScheduleRiskCoordinates));
    }

    [Test]
    public async Task Explicit_deadline_has_no_inferred_day_cap_and_does_not_supply_an_indefinite_sanction_end()
    {
        var (review, sanction, coordinates, _, _) = Create(indefinite: true);
        review.Rebind(Coordinates(coordinates, 0), Now.AddDays(120), 1, Now);
        await Assert.That(review.DueAt).IsEqualTo(Now.AddDays(120));
        await Assert.That(review.OriginalSanctionEndsAt).IsNull();
        await Assert.That(sanction.EndsAt).IsNull();
        await Assert.That(sanction.ReviewAt).IsEqualTo(Now.AddHours(1));
        await Assert.That(sanction.Revision).IsEqualTo(1L);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task Terminal_acceptance_or_stale_revision_cannot_record_a_new_review_intent(int change)
    {
        var (review, _, coordinates, finding, reviewer) = Create();
        if (change == 0)
            review.Accept(Guid.CreateVersion7(), finding, reviewer, reviewer.Revision, coordinates, 1, Now);
        var expected = change == 0 ? 2 : change == 1 ? 2 : 1;
        var observed = change == 2 ? Now.AddTicks(-1) : Now;
        await Assert.That(() => review.Accept(Guid.CreateVersion7(), finding, reviewer,
            reviewer.Revision, coordinates, expected, observed)).Throws<InvalidOperationException>();
        await Assert.That(review.AcceptedFindingId).IsEqualTo(change == 0 ? finding.Id : (Guid?)null);
        await Assert.That(review.Revision).IsEqualTo(change == 0 ? 2L : 1L);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    public async Task Rebinding_cannot_regress_any_bound_revision(int change)
    {
        var (review, _, coordinates, _, _) = Create();
        var advanced = ModerationScheduleRiskCoordinates.Create(coordinates.EventScope, coordinates.SessionId, Guid.CreateVersion7(),
            coordinates.ParticipantId, coordinates.Participant, Guid.NewGuid(),
            coordinates.PolicyId, 3, Guid.CreateVersion7(),
            ModerationRuleReference.Create(coordinates.Policy.OwnerId, coordinates.Policy.Code, 3));
        review.Rebind(advanced, Now.AddHours(4), 1, Now);
        var regressed = ModerationScheduleRiskCoordinates.Create(coordinates.EventScope, coordinates.SessionId, Guid.CreateVersion7(),
            coordinates.ParticipantId, coordinates.Participant, Guid.NewGuid(),
            coordinates.PolicyId, change == 0 ? 2 : 4, Guid.CreateVersion7(),
            ModerationRuleReference.Create(coordinates.Policy.OwnerId, coordinates.Policy.Code, change == 1 ? 2 : 4));
        await Assert.That(() => review.Rebind(regressed, Now.AddHours(5), 2, Now))
            .Throws<InvalidOperationException>();
        await Assert.That(review.Coordinates).IsEqualTo(advanced);
        await Assert.That(review.Revision).IsEqualTo(2L);
    }

    [Test]
    public async Task Schedule_binding_requires_an_opaque_nonempty_stamp_without_numeric_ordering()
    {
        var (_, _, coordinates, _, _) = Create();
        await Assert.That(() => ModerationScheduleRiskCoordinates.Create(coordinates.EventScope, coordinates.SessionId, Guid.Empty,
            coordinates.ParticipantId, coordinates.Participant, coordinates.ParticipantGeneration,
            coordinates.PolicyId, coordinates.PolicyRevision, coordinates.PublicationId, coordinates.Policy))
            .Throws<ArgumentException>();
        await Assert.That(() => ModerationScheduleRiskCoordinates.Create(coordinates.EventScope, coordinates.SessionId,
            coordinates.ScheduleGeneration, coordinates.ParticipantId, coordinates.Participant, Guid.Empty,
            coordinates.PolicyId, coordinates.PolicyRevision, coordinates.PublicationId, coordinates.Policy))
            .Throws<ArgumentException>();
        var stamp = Guid.NewGuid();
        var bound = ModerationScheduleRiskCoordinates.Create(coordinates.EventScope, coordinates.SessionId, stamp,
            coordinates.ParticipantId, coordinates.Participant, stamp,
            coordinates.PolicyId, coordinates.PolicyRevision, coordinates.PublicationId, coordinates.Policy);
        await Assert.That(bound.ScheduleGeneration).IsEqualTo(stamp);
        await Assert.That(bound.ParticipantGeneration).IsEqualTo(stamp);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task Adopted_publication_identity_is_required_independently_of_the_rule(int change)
    {
        var (_, _, coordinates, _, _) = Create();
        await Assert.That(() => ModerationScheduleRiskCoordinates.Create(coordinates.EventScope,
            coordinates.SessionId, coordinates.ScheduleGeneration, coordinates.ParticipantId,
            coordinates.Participant, coordinates.ParticipantGeneration,
            change == 0 ? Guid.Empty : coordinates.PolicyId,
            change == 1 ? 0 : coordinates.PolicyRevision,
            change == 2 ? Guid.Empty : coordinates.PublicationId, coordinates.Policy))
            .Throws<ArgumentException>();
    }

    private static ModerationScheduleRiskCoordinates Coordinates(ModerationScheduleRiskCoordinates source, int change) =>
        ModerationScheduleRiskCoordinates.Create(
            change == 3 ? AuthorityScope.Event(source.EventScope.Parent!, Guid.CreateVersion7()) : source.EventScope,
            change == 5 ? Guid.CreateVersion7() : source.SessionId,
            change == 0 ? Guid.CreateVersion7() : source.ScheduleGeneration,
            change == 4 ? Guid.CreateVersion7() : source.ParticipantId,
            source.Participant, change == 1 ? Guid.NewGuid() : source.ParticipantGeneration,
            change == 6 ? Guid.CreateVersion7() : source.PolicyId,
            source.PolicyRevision + (change == 7 ? 1 : 0),
            change == 8 ? Guid.CreateVersion7() : source.PublicationId,
            ModerationRuleReference.Create(source.Policy.OwnerId, source.Policy.Code,
                source.Policy.Revision + (change == 2 ? 1 : 0)));

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task Persisted_review_preserves_ancestry_for_every_lifecycle_state(int transition)
    {
        var (review, _, coordinates, finding, reviewer) = Create();
        if (transition == 1) review.Rebind(Coordinates(coordinates, 0), Now.AddHours(4), 1, Now);
        if (transition == 2)
            review.Accept(Guid.CreateVersion7(), finding, reviewer, reviewer.Revision, coordinates, 1, Now);
        if (transition == 3) review.Withdraw(Guid.CreateVersion7(), review.Owner, 1, Now);
        if (transition == 4) review.Reconcile(1, review.DueAt);
        review.ValidatePersistedState();
        await Assert.That(review.OriginalCoordinates).IsEqualTo(coordinates);
        await Assert.That(review.OriginalSanctionEndsAt).IsEqualTo(Now.AddHours(1));
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
    [Arguments(12)]
    [Arguments(13)]
    [Arguments(14)]
    [Arguments(15)]
    public async Task Corrupt_source_deadline_state_or_terminal_provenance_cannot_be_used(int corruption)
    {
        var (review, _, coordinates, _, _) = Create();
        var (property, value) = corruption switch
        {
            0 => (nameof(review.SourceSanctionId), (object)Guid.Empty),
            1 => (nameof(review.SourceSanctionRevision), 0L),
            2 => (nameof(review.SourceDecisionId), Guid.Empty),
            3 => (nameof(review.OriginalSanctionEndsAt), Now.AddTicks(-1)),
            4 => (nameof(review.OriginalDueAt), Now),
            5 => (nameof(review.DueAt), Now),
            6 => (nameof(review.State), (ModerationScheduleRiskReviewState)0),
            7 => (nameof(review.Revision), 0L),
            8 => (nameof(review.UpdatedAt), Now.AddTicks(-1)),
            9 => (nameof(review.AcceptedFindingId), Guid.CreateVersion7()),
            10 => (nameof(review.WithdrawalIntentId), Guid.CreateVersion7()),
            11 => ("PolicyRevision", 0L),
            12 => (nameof(review.State), ModerationScheduleRiskReviewState.Accepted),
            13 => (nameof(review.UpdatedAt), review.DueAt),
            14 => (nameof(review.State), ModerationScheduleRiskReviewState.Overdue),
            _ => (nameof(review.Owner), SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(),
                AuthorityScope.TrustDomain(Guid.CreateVersion7())))
        };
        typeof(ModerationScheduleRiskReview).GetProperty(property)!.SetValue(review, value);
        await Assert.That(() => review.ValidatePersistedState()).Throws<ArgumentException>();
        await Assert.That(() => review.IsOverdueAt(Now)).Throws<ArgumentException>();
        await Assert.That(() => review.Reconcile(review.Revision, Now)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task Accepted_provenance_is_complete_and_cannot_regress_to_pending(int corruption)
    {
        var (review, _, coordinates, finding, reviewer) = Create();
        review.Accept(Guid.CreateVersion7(), finding, reviewer, reviewer.Revision, coordinates, 1, Now);
        var property = corruption switch
        {
            0 => nameof(review.AcceptedIntentId),
            1 => nameof(review.AcceptedFindingRevision),
            2 => nameof(review.AcceptedReviewerAssignmentRevision),
            _ => nameof(review.State)
        };
        object? value = corruption == 3 ? ModerationScheduleRiskReviewState.Pending : null;
        typeof(ModerationScheduleRiskReview).GetProperty(property)!.SetValue(review, value);
        await Assert.That(() => review.ValidatePersistedState()).Throws<ArgumentException>();
    }

    private static (ModerationScheduleRiskReview Review, ModerationSanction Sanction,
        ModerationScheduleRiskCoordinates Coordinates, ModerationFinding Finding,
        ModerationReviewerAssignment Reviewer) Create(bool indefinite = false)
    {
        var instance = AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
            Guid.CreateVersion7());
        var scope = AuthorityScope.Tenant(instance, Guid.CreateVersion7());
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance);
        var issuer = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance);
        var owner = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance);
        var rule = ModerationRuleReference.Create(scope.Id, "event-risk", 1);
        var sourceCase = ModerationCase.Open(Guid.CreateVersion7(), subject, scope, Guid.CreateVersion7(), Now);
        var decision = ModerationDecision.Propose(Guid.CreateVersion7(), sourceCase.Id, subject, issuer,
            instance, scope, rule, [ModerationCapability.Publish], Now,
            indefinite ? null : Now.AddHours(1), indefinite ? Now.AddHours(1) : null, indefinite ? 2 : 1, Now);
        decision.RecordResponse(1, Now);
        var sourceFinding = ModerationFinding.Finalize(Guid.CreateVersion7(), sourceCase, 1, scope, null, null,
            rule, rule, rule, rule, owner,
            ModerationFindingEvidenceReference.Response(Guid.CreateVersion7(), sourceCase.Id, 1, Now),
            "Original scoped finding source facts.", Now);
        decision.AcceptFinding(sourceFinding.Id, 2, Now);
        decision.Approve(owner, scope, 3, Now);
        if (indefinite)
            decision.Approve(SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance),
                scope, 4, Now);
        var sanction = decision.Activate(Guid.CreateVersion7(), indefinite ? 5 : 4, Now);
        var coordinates = ModerationScheduleRiskCoordinates.Create(AuthorityScope.Event(scope, Guid.CreateVersion7()),
            Guid.CreateVersion7(), Guid.NewGuid(), Guid.CreateVersion7(), subject, Guid.NewGuid(),
            Guid.CreateVersion7(), 1, Guid.CreateVersion7(), rule);
        var review = ModerationScheduleRiskReview.Open(Guid.CreateVersion7(), sanction, 1,
            coordinates, owner, owner, Now.AddHours(3), Now);
        var riskCase = ModerationCase.Open(Guid.CreateVersion7(), subject, scope, Guid.CreateVersion7(), Now);
        var adjudicator = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance);
        var evidence = ModerationFindingEvidenceReference.Response(Guid.CreateVersion7(), riskCase.Id, 1, Now);
        var finding = ModerationFinding.Finalize(Guid.CreateVersion7(), riskCase, 1, scope, null, null,
            rule, rule, rule, rule, adjudicator, evidence, "Review source facts; no native permission asserted.", Now);
        var reviewer = ModerationReviewerAssignment.Assign(Guid.CreateVersion7(), riskCase, 1,
            adjudicator.Id, owner.Id, Now);
        reviewer.QualifyController(1, adjudicator.Id, Guid.CreateVersion7(), owner.Id, Now.AddMinutes(30), Now);
        reviewer.RecordConflictAssessment(2, ModerationReviewerConflictState.Independent,
            Guid.CreateVersion7(), owner.Id, Now);
        return (review, sanction, coordinates, finding, reviewer);
    }
}
