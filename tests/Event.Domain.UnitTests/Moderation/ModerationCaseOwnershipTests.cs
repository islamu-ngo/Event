using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public class ModerationCaseOwnershipTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(ModerationCaseTargetKind.ParticipantIncident)]
    [Arguments(ModerationCaseTargetKind.LocalIncident)]
    public async Task TargetOwnedIntakeDoesNotInventSubjectAttribution(ModerationCaseTargetKind kind)
    {
        var scope = Scope();
        var evidence = Evidence(scope, kind);
        var allegation = ModerationCase.OpenAllegation(Guid.CreateVersion7(), scope, evidence, Now);

        allegation.ValidatePersistedState();
        await Assert.That(allegation.Subject).IsNull();
        await Assert.That(allegation.SubjectId).IsNull();
        await Assert.That(allegation.SubjectKind).IsNull();
        await Assert.That(allegation.Target).IsEqualTo(evidence.Target);
        await Assert.That(allegation.OccurrenceId).IsEqualTo(evidence.OccurrenceId);
        await Assert.That(allegation.Evidence.Single().ToReference(allegation.Target!, allegation.OccurrenceId))
            .IsEqualTo(evidence);
    }

    [Test]
    public async Task NewMaterialFactsAdvanceRevisionButExactProvenanceDoesNot()
    {
        var scope = Scope();
        var first = Evidence(scope);
        var allegation = ModerationCase.OpenAllegation(Guid.CreateVersion7(), scope, first, Now);
        var second = ModerationEvidenceReference.Create(first.Target, first.SourceOrigin, first.OccurrenceId,
            first.SourceId, first.SourceVersion, Guid.CreateVersion7());

        allegation.RecordEvidence(second, 1, Now.AddMinutes(1));
        allegation.RecordEvidence(second, 2, Now.AddMinutes(2));
        allegation.ValidatePersistedState();

        await Assert.That(allegation.Subject).IsNull();
        await Assert.That(allegation.Revision).IsEqualTo(2L);
        await Assert.That(allegation.Evidence.Count).IsEqualTo(2);
        await Assert.That(allegation.UpdatedAt).IsEqualTo(Now.AddMinutes(1));
        await Assert.That(allegation.Evidence.First().ToReference(first.Target, first.OccurrenceId)).IsEqualTo(first);
        allegation.Close(2, Now.AddMinutes(3));
        allegation.ValidatePersistedState();
        await Assert.That(allegation.Revision).IsEqualTo(3L);
        await Assert.That(allegation.Evidence.Count).IsEqualTo(2);
    }

    [Test]
    public async Task NewFactsAfterClosureRetainEvidenceWithoutReopeningOrAttribution()
    {
        var scope = Scope();
        var first = Evidence(scope);
        var allegation = ModerationCase.OpenAllegation(Guid.CreateVersion7(), scope, first, Now);
        allegation.Close(1, Now.AddMinutes(1));
        var second = ModerationEvidenceReference.Create(first.Target, scope, first.OccurrenceId,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        allegation.RecordEvidence(second, 2, Now.AddMinutes(2));
        allegation.RecordEvidence(second, 3, Now.AddMinutes(3));
        allegation.ValidatePersistedState();

        await Assert.That(allegation.State).IsEqualTo(ModerationCaseState.Closed);
        await Assert.That(allegation.Subject).IsNull();
        await Assert.That(allegation.Revision).IsEqualTo(3L);
        await Assert.That(allegation.Evidence.Select(row => row.CaseRevision)).IsEquivalentTo(new long[] { 1, 3 });
        await Assert.That(allegation.UpdatedAt).IsEqualTo(Now.AddMinutes(2));
        await Assert.That(() => allegation.Close(3, Now.AddMinutes(4))).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ChangedOccurrenceOrTargetCannotBeAttachedToExistingCase(bool occurrence)
    {
        var scope = Scope();
        var first = Evidence(scope);
        var allegation = ModerationCase.OpenAllegation(Guid.CreateVersion7(), scope, first, Now);
        var changed = ModerationEvidenceReference.Create(
            occurrence ? first.Target : ModerationCaseTarget.Create(first.Target.Kind,
                first.Target.Id, first.Target.Origin, Guid.CreateVersion7()),
            first.SourceOrigin, occurrence ? Guid.CreateVersion7() : first.OccurrenceId,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        await Assert.That(() => allegation.RecordEvidence(changed, 1, Now.AddMinutes(1)))
            .Throws<ArgumentException>();
        await Assert.That(allegation.Revision).IsEqualTo(1L);
        await Assert.That(allegation.Evidence.Count).IsEqualTo(1);
    }

    [Test]
    public async Task StaleEvidenceWriteCannotMutateTheCase()
    {
        var scope = Scope();
        var first = Evidence(scope);
        var allegation = ModerationCase.OpenAllegation(Guid.CreateVersion7(), scope, first, Now);
        var second = ModerationEvidenceReference.Create(first.Target, first.SourceOrigin, first.OccurrenceId,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        allegation.RecordEvidence(second, 1, Now.AddMinutes(1));
        await Assert.That(() => allegation.RecordEvidence(first, 1, Now.AddMinutes(2)))
            .Throws<InvalidOperationException>();
        await Assert.That(allegation.Revision).IsEqualTo(2L);
    }

    [Test]
    public async Task UnattributedCasesAllowIntakeAssignmentButNeverInventQualifiedAdjudicationOrFinding()
    {
        var scope = Scope();
        var allegation = ModerationCase.OpenAllegation(Guid.CreateVersion7(), scope, Evidence(scope), Now);
        var rule = ModerationRuleReference.Create(Guid.CreateVersion7(), "intake-test", 1);
        var reviewer = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope.Parent!);

        var intake = ModerationReviewerAssignment.Assign(
            Guid.CreateVersion7(), allegation, 1, reviewer.Id, Guid.CreateVersion7(), Now);
        await Assert.That(intake.SubjectHumanId).IsNull();
        await Assert.That(intake.ConflictState).IsEqualTo(ModerationReviewerConflictState.Unresolved);
        await Assert.That(intake.IsQualifiedAt(Now)).IsFalse();
        await Assert.That(() => ModerationFinding.Finalize(
            Guid.CreateVersion7(), allegation, 1, rule, rule, reviewer,
            ModerationFindingEvidenceReference.Response(Guid.CreateVersion7(), allegation.Id, 1, Now),
            "An allegation is not an attributed finding.", Now)).Throws<InvalidOperationException>();
        await Assert.That(allegation.Subject).IsNull();
    }

    [Test]
    public async Task ExistingAttributedCasesRetainTheirOriginalFindingRevision()
    {
        var scope = Scope();
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope.Parent!);
        var allegation = ModerationCase.Open(Guid.CreateVersion7(), subject, scope, Guid.CreateVersion7(), Now);
        var reviewer = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope.Parent!);
        var rule = ModerationRuleReference.Create(Guid.CreateVersion7(), "existing-test", 1);
        var finding = ModerationFinding.Finalize(Guid.CreateVersion7(), allegation, 1,
            rule, rule, reviewer,
            ModerationFindingEvidenceReference.Response(Guid.CreateVersion7(), allegation.Id, 1, Now),
            "Retained owned final finding.", Now);

        finding.ValidatePersistedState();
        await Assert.That(finding.Subject).IsEqualTo(subject);
        await Assert.That(finding.CaseRevision).IsEqualTo(1L);
        await Assert.That(allegation.Target).IsNull();
        await Assert.That(() => allegation.RecordEvidence(Evidence(scope), 1, Now))
            .Throws<InvalidOperationException>();
        allegation.Close(1, Now.AddMinutes(1));
        allegation.ValidatePersistedState();
        finding.ValidatePersistedState();
        await Assert.That(finding.CaseRevision).IsEqualTo(1L);
    }

    [Test]
    public async Task InstanceTargetDoesNotPermitEvidenceFromAnotherTenant()
    {
        var scope = Scope();
        var target = ModerationCaseTarget.Create(
            ModerationCaseTargetKind.Actor, Guid.CreateVersion7(), scope.Parent!, Guid.CreateVersion7());
        var otherTenant = AuthorityScope.Tenant(scope.Parent!, Guid.CreateVersion7());
        var evidence = ModerationEvidenceReference.Create(target, otherTenant,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        await Assert.That(() => ModerationCase.OpenAllegation(Guid.CreateVersion7(), scope, evidence, Now))
            .Throws<ArgumentException>();
    }

    private static ModerationEvidenceReference Evidence(
        AuthorityScope scope, ModerationCaseTargetKind kind = ModerationCaseTargetKind.LocalIncident) =>
        ModerationEvidenceReference.Create(
            ModerationCaseTarget.Create(kind, Guid.CreateVersion7(), scope, Guid.CreateVersion7()), scope,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

    private static AuthorityScope Scope() => AuthorityScope.Tenant(
        AuthorityScope.Instance(AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()),
            Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
}
