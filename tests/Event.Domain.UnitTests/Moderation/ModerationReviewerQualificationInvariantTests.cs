using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public class ModerationReviewerQualificationInvariantTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task TargetOwnedIntakeAssignmentDoesNotInventResponsibilityOrQualifiedReview()
    {
        var scope = AuthorityScope.Tenant(AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()),
                Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        var evidence = ModerationEvidenceReference.Create(
            ModerationCaseTarget.Create(ModerationCaseTargetKind.LocalIncident,
                Guid.CreateVersion7(), scope, Guid.CreateVersion7()), scope,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var moderationCase = ModerationCase.OpenAllegation(Guid.CreateVersion7(), scope, evidence, Now);

        var assignment = Assign(moderationCase);

        await Assert.That(assignment.CaseId).IsEqualTo(moderationCase.Id);
        await Assert.That(assignment.CaseTenantId).IsEqualTo(scope.Id);
        await Assert.That(assignment.SubjectHumanId).IsNull();
        await Assert.That(assignment.ConflictState).IsEqualTo(ModerationReviewerConflictState.Unresolved);
        await Assert.That(assignment.IsQualifiedAt(Now)).IsFalse();
        await Assert.That(moderationCase.Subject).IsNull();
        await Assert.That(moderationCase.Target).IsEqualTo(evidence.Target);
        await Assert.That(moderationCase.Revision).IsEqualTo(1L);
        assignment.ValidatePersistedState();
        moderationCase.ValidatePersistedState();
    }

    [Test]
    public async Task AssignmentPreservesTheActualCaseTenantForNativeFiltering()
    {
        var scope = AuthorityScope.Tenant(AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()),
                Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        var moderationCase = ModerationCase.Open(Guid.CreateVersion7(),
            SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope),
            scope, Guid.CreateVersion7(), Now);
        var assignment = Assign(moderationCase);
        await Assert.That(assignment.CaseTenantId).IsEqualTo(scope.Id);
        await Assert.That(assignment.TrustDomainId).IsEqualTo(scope.TrustDomainId);
        assignment.ValidatePersistedState();
    }

    [Test]
    public async Task AssignmentAndControllerIdentityDoNotByThemselvesQualifyReview()
    {
        var assignment = Assign(Case());
        await Assert.That(assignment.IsQualifiedAt(Now)).IsFalse();
        assignment.QualifyController(1, assignment.ReviewerHumanId, Guid.CreateVersion7(),
            Guid.CreateVersion7(), Now.AddHours(1), Now);
        await Assert.That(assignment.IsQualifiedAt(Now)).IsFalse();
        assignment.RecordConflictAssessment(2, ModerationReviewerConflictState.Independent,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Now);
        await Assert.That(assignment.IsQualifiedAt(Now)).IsTrue();
        assignment.ValidatePersistedState();
    }

    [Test]
    public async Task TwoDistinctAccountsWithOneControllerCannotSupplyIndependentQuorum()
    {
        var moderationCase = Case();
        var controller = Guid.CreateVersion7();
        var first = Qualify(Assign(moderationCase), controller);
        var second = Qualify(Assign(moderationCase), controller);
        await Assert.That(first.IsQualifiedAt(Now)).IsTrue();
        await Assert.That(second.IsQualifiedAt(Now)).IsTrue();
        await Assert.That(first.IsIndependentOf(second, Now)).IsFalse();
        await Assert.That(second.IsIndependentOf(first, Now)).IsFalse();
    }

    [Test]
    public async Task IndependentQualifiedHumansAreCaseSpecific()
    {
        var moderationCase = Case();
        var first = Qualify(Assign(moderationCase));
        var second = Qualify(Assign(moderationCase));
        await Assert.That(first.IsIndependentOf(second, Now)).IsTrue();
        await Assert.That(first.IsIndependentOf(Qualify(Assign(Case())), Now)).IsFalse();
        await Assert.That(first.IsIndependentOf(first, Now)).IsFalse();
    }

    [Test]
    public async Task ChangingControllerInvalidatesOldConflictEvidence()
    {
        var assignment = Qualify(Assign(Case()));
        assignment.QualifyController(3, Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Now.AddHours(2), Now.AddMinutes(1));
        await Assert.That(assignment.IsQualifiedAt(Now.AddMinutes(1))).IsFalse();
        await Assert.That(assignment.ConflictEvidenceId).IsNull();
        await Assert.That(assignment.AssessedByHumanId).IsNull();
        await Assert.That(assignment.ConflictState).IsEqualTo(ModerationReviewerConflictState.Unresolved);
        assignment.ValidatePersistedState();
    }

    [Test]
    public async Task QualificationExpiresAtTheExactBoundaryWithoutASweep()
    {
        var assignment = Qualify(Assign(Case()));
        await Assert.That(assignment.IsQualifiedAt(Now.AddHours(1).AddTicks(-1))).IsTrue();
        await Assert.That(assignment.IsQualifiedAt(Now.AddHours(1))).IsFalse();
        await Assert.That(assignment.IsQualifiedAt(Now.AddTicks(-1))).IsFalse();
    }

    [Test]
    public async Task WithdrawalCannotBeReversedByRequalification()
    {
        var assignment = Qualify(Assign(Case()));
        var actor = Guid.CreateVersion7();
        assignment.Withdraw(3, actor, Now.AddMinutes(1));
        await Assert.That(assignment.IsQualifiedAt(Now.AddMinutes(1))).IsFalse();
        await Assert.That(() => assignment.QualifyController(4, assignment.ReviewerHumanId,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Now.AddHours(2), Now.AddMinutes(2)))
            .Throws<InvalidOperationException>();
        await Assert.That(assignment.WithdrawnByHumanId).IsEqualTo(actor);
        await Assert.That(assignment.Revision).IsEqualTo(4L);
        assignment.ValidatePersistedState();
    }

    [Test]
    public async Task SubjectAndItsKnownControllerCannotReviewTheCase()
    {
        var moderationCase = Case();
        var subject = moderationCase.Subject ?? throw new InvalidOperationException("The fixture requires an attributed case.");
        await Assert.That(() => ModerationReviewerAssignment.Assign(Guid.CreateVersion7(),
            moderationCase, 1, subject.Id, Guid.CreateVersion7(), Now))
            .Throws<ArgumentException>();
        var assignment = Assign(moderationCase);
        await Assert.That(() => assignment.QualifyController(1, subject.Id,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Now.AddHours(1), Now)).Throws<ArgumentException>();
        await Assert.That(assignment.Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task ReviewerCannotCertifyItsOwnIndependence()
    {
        var assignment = Assign(Case());
        assignment.QualifyController(1, assignment.ReviewerHumanId, Guid.CreateVersion7(),
            Guid.CreateVersion7(), Now.AddHours(1), Now);
        await Assert.That(() => assignment.RecordConflictAssessment(2,
            ModerationReviewerConflictState.Independent, Guid.CreateVersion7(),
            assignment.ReviewerHumanId, Now)).Throws<ArgumentException>();
        await Assert.That(assignment.Revision).IsEqualTo(2L);
        await Assert.That(assignment.IsQualifiedAt(Now)).IsFalse();
    }

    [Test]
    public async Task StaleAndInvalidChangesPreserveAcceptedQualification()
    {
        var assignment = Qualify(Assign(Case()));
        var evidence = assignment.ConflictEvidenceId;
        await Assert.That(() => assignment.Withdraw(2, Guid.CreateVersion7(), Now))
            .Throws<InvalidOperationException>();
        await Assert.That(() => assignment.QualifyController(3, Guid.CreateVersion7(),
            Guid.Empty, Guid.CreateVersion7(), Now.AddHours(1), Now)).Throws<ArgumentException>();
        await Assert.That(assignment.ConflictEvidenceId).IsEqualTo(evidence);
        await Assert.That(assignment.Revision).IsEqualTo(3L);
        await Assert.That(assignment.IsQualifiedAt(Now)).IsTrue();
    }

    [Test]
    public async Task ConflictedAndUnresolvedAssignmentsCannotSupplyQuorum()
    {
        var moderationCase = Case();
        var first = Qualify(Assign(moderationCase));
        var other = Qualify(Assign(moderationCase));
        other.RecordConflictAssessment(3, ModerationReviewerConflictState.Conflicted,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Now);
        await Assert.That(other.IsQualifiedAt(Now)).IsFalse();
        await Assert.That(first.IsIndependentOf(other, Now)).IsFalse();
        await Assert.That(first.IsIndependentOf(Assign(moderationCase), Now)).IsFalse();
        other.ValidatePersistedState();
    }

    [Test]
    public async Task RevisionOverflowCannotPartiallyWithdrawQualifiedReviewer()
    {
        var assignment = Qualify(Assign(Case()));
        typeof(ModerationReviewerAssignment).GetProperty(nameof(ModerationReviewerAssignment.Revision))!
            .SetValue(assignment, long.MaxValue);
        await Assert.That(() => assignment.Withdraw(long.MaxValue, Guid.CreateVersion7(), Now))
            .Throws<OverflowException>();
        await Assert.That(assignment.WithdrawnAt).IsNull();
        await Assert.That(assignment.Revision).IsEqualTo(long.MaxValue);
        await Assert.That(assignment.IsQualifiedAt(Now)).IsTrue();
    }

    [Test]
    public async Task ClosedOrStaleCaseCannotAssignANewReviewer()
    {
        var moderationCase = Case();
        await Assert.That(() => ModerationReviewerAssignment.Assign(Guid.CreateVersion7(),
            moderationCase, 2, Guid.CreateVersion7(), Guid.CreateVersion7(), Now)).Throws<ArgumentException>();
        moderationCase.Close(1, Now);
        await Assert.That(() => Assign(moderationCase)).Throws<ArgumentException>();
    }

    private static ModerationCase Case()
    {
        var scope = AuthorityScope.TrustDomain(Guid.CreateVersion7());
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope);
        return ModerationCase.Open(Guid.CreateVersion7(), subject, scope, Guid.CreateVersion7(), Now);
    }

    private static ModerationReviewerAssignment Assign(ModerationCase moderationCase) =>
        ModerationReviewerAssignment.Assign(Guid.CreateVersion7(), moderationCase, 1,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Now);

    private static ModerationReviewerAssignment Qualify(
        ModerationReviewerAssignment assignment, Guid? controllerId = null)
    {
        assignment.QualifyController(1, controllerId ?? assignment.ReviewerHumanId,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Now.AddHours(1), Now);
        assignment.RecordConflictAssessment(2, ModerationReviewerConflictState.Independent,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Now);
        return assignment;
    }
}
