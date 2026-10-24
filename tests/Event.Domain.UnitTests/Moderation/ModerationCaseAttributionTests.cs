using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationCaseAttributionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task SeparateSubjectsRetainOneOccurrenceWithoutChangingConfidentialRoot()
    {
        var root = Root();
        var first = Attribute(root, Guid.CreateVersion7());
        var second = Attribute(root, Guid.CreateVersion7());

        first.Close(1, Now.AddMinutes(1));

        root.ValidatePersistedState();
        first.ValidatePersistedState();
        second.ValidatePersistedState();
        await Assert.That(root.Subject).IsNull();
        await Assert.That(root.Revision).IsEqualTo(1L);
        await Assert.That(root.Evidence.Count).IsEqualTo(1);
        await Assert.That(first.OccurrenceId).IsEqualTo(root.OccurrenceId);
        await Assert.That(second.OccurrenceId).IsEqualTo(root.OccurrenceId);
        await Assert.That(first.OccurrenceCaseId).IsEqualTo(root.Id);
        await Assert.That(first.OccurrenceEvidenceRevision).IsEqualTo(1L);
        await Assert.That(first.Target).IsNull();
        await Assert.That(first.Evidence.Count).IsEqualTo(0);
        await Assert.That(second.Subject).IsNotEqualTo(first.Subject);
        await Assert.That(second.State).IsEqualTo(ModerationCaseState.Open);
        await Assert.That(second.Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task LaterIntakeCannotRebindTheAttributedEvidenceRevision()
    {
        var root = Root();
        var child = Attribute(root, Guid.CreateVersion7());
        var original = root.Evidence.Single().ToReference(root.Target!, root.OccurrenceId);
        root.RecordEvidence(ModerationEvidenceReference.Create(original.Target, original.SourceOrigin,
            original.OccurrenceId, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()), 1, Now.AddMinutes(1));

        child.ValidatePersistedState();
        await Assert.That(child.OccurrenceEvidenceRevision).IsEqualTo(1L);
        await Assert.That(child.Revision).IsEqualTo(1L);
        await Assert.That(root.Revision).IsEqualTo(2L);
    }

    [Test]
    public async Task ClosureRevisionAndSubjectCaseCannotMasqueradeAsOccurrenceEvidence()
    {
        var root = Root();
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), root.Scope.Parent!);
        var child = Attribute(root, subject.Id);
        root.Close(1, Now.AddMinutes(1));

        await Assert.That(() => ModerationCase.OpenAttributed(Guid.CreateVersion7(), subject, root,
            2, Guid.CreateVersion7(), Now.AddMinutes(2))).Throws<ArgumentException>();
        await Assert.That(() => ModerationCase.OpenAttributed(Guid.CreateVersion7(), subject, child,
            1, Guid.CreateVersion7(), Now.AddMinutes(2))).Throws<ArgumentException>();
    }

    [Test]
    public async Task ForeignSubjectOriginCannotBeAttachedToAnOccurrence()
    {
        var root = Root();
        var foreign = AuthorityScope.Instance(root.Scope.Parent!.Parent!, Guid.CreateVersion7());
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), foreign);
        await Assert.That(() => ModerationCase.OpenAttributed(Guid.CreateVersion7(), subject, root,
            1, Guid.CreateVersion7(), Now)).Throws<ArgumentException>();
    }

    [Test]
    public async Task ReversingOneSubjectFindingDoesNotReverseAnotherSubjectsFinding()
    {
        var root = Root();
        var firstCase = Attribute(root, Guid.CreateVersion7());
        var secondCase = Attribute(root, Guid.CreateVersion7());
        var rule = ModerationRuleReference.Create(Guid.CreateVersion7(), "Attribution.Rule", 1);
        var reason = ModerationRuleReference.Create(Guid.CreateVersion7(), "Attribution.Reason", 1);
        var reviewer = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), root.Scope.Parent!);
        ModerationFinding Finding(ModerationCase owner) => ModerationFinding.Finalize(
            Guid.CreateVersion7(), owner, 1, owner.Scope, owner.AttributionSourceId, 1,
            rule, rule, reason, reason, reviewer,
            ModerationFindingEvidenceReference.Response(Guid.CreateVersion7(), owner.Id, 1, Now),
            "This conclusion concerns only the individually qualified subject.", Now);
        var first = Finding(firstCase);
        var second = Finding(secondCase);

        first.Reverse(Guid.CreateVersion7(), reviewer, "This subject-specific conclusion was corrected.", 1, Now.AddMinutes(1));

        await Assert.That(first.State).IsEqualTo(ModerationFindingState.Reversed);
        await Assert.That(second.State).IsEqualTo(ModerationFindingState.Final);
        await Assert.That(second.Revision).IsEqualTo(1L);
        await Assert.That(root.Revision).IsEqualTo(1L);
        await Assert.That(root.Subject).IsNull();
    }

    private static ModerationCase Attribute(ModerationCase root, Guid human) =>
        ModerationCase.OpenAttributed(Guid.CreateVersion7(),
            SubjectReference.Create(ModerationSubjectKind.Human, human, root.Scope.Parent!),
            root, 1, Guid.CreateVersion7(), Now);

    private static ModerationCase Root()
    {
        var scope = AuthorityScope.Tenant(AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        var evidence = ModerationEvidenceReference.Create(ModerationCaseTarget.Create(
            ModerationCaseTargetKind.EventVersion, Guid.CreateVersion7(), scope, Guid.CreateVersion7()),
            scope, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        return ModerationCase.OpenAllegation(Guid.CreateVersion7(), scope, evidence, Now);
    }
}
