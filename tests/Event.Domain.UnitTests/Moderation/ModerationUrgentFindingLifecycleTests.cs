using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationUrgentFindingLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Qualified_urgent_finding_advances_without_recording_a_subject_response()
    {
        var (decision, finding) = Create(true);
        decision.AcceptUrgentFinding(finding, 1, Now);
        await Assert.That(decision.State).IsEqualTo(ModerationDecisionState.AwaitingApproval);
        await Assert.That(decision.Revision).IsEqualTo(2L);
        await Assert.That(decision.FindingId).IsEqualTo(finding.Id);
        await Assert.That(decision.Approvals.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task Other_evidence_or_changed_finding_cannot_bypass_response(int changed)
    {
        var (decision, finding) = Create(changed != 0);
        if (changed == 1) finding = Create(true).Finding;
        if (changed == 2)
            finding.Reverse(Guid.CreateVersion7(), finding.Adjudicator, "Qualified reversal.", 1, Now);
        var expectedRevision = changed == 3 ? 2 : 1;
        await Assert.That(() => decision.AcceptUrgentFinding(finding, expectedRevision, Now))
            .Throws<InvalidOperationException>();
        await Assert.That(decision.State).IsEqualTo(ModerationDecisionState.Proposed);
        await Assert.That(decision.Revision).IsEqualTo(1L);
        await Assert.That(decision.FindingId).IsNull();
    }

    [Test]
    public async Task Future_finding_cannot_be_accepted_before_its_finalization()
    {
        var (decision, finding) = Create(true);
        await Assert.That(() => decision.AcceptUrgentFinding(finding, 1, Now.AddTicks(-1)))
            .Throws<InvalidOperationException>();
        await Assert.That(decision.State).IsEqualTo(ModerationDecisionState.Proposed);
        await Assert.That(decision.FindingId).IsNull();
    }

    [Test]
    public async Task Issuer_with_another_origin_is_not_an_independent_urgent_adjudicator()
    {
        var (decision, finding) = Create(true, true);
        await Assert.That(() => decision.AcceptUrgentFinding(finding, 1, Now))
            .Throws<InvalidOperationException>();
        await Assert.That(decision.State).IsEqualTo(ModerationDecisionState.Proposed);
        await Assert.That(decision.Revision).IsEqualTo(1L);
        await Assert.That(decision.FindingId).IsNull();
    }

    private static (ModerationDecision Decision, ModerationFinding Finding) Create(bool urgent, bool issuerAlias = false)
    {
        var instance = AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
            Guid.CreateVersion7());
        var scope = AuthorityScope.Tenant(instance, Guid.CreateVersion7());
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance);
        var issuer = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance);
        var adjudicator = SubjectReference.Create(ModerationSubjectKind.Human,
            issuerAlias ? issuer.Id : Guid.CreateVersion7(), issuerAlias ? scope : instance);
        var allegation = ModerationCase.Open(
            Guid.CreateVersion7(), subject, scope, Guid.CreateVersion7(), Now.AddMinutes(-2));
        var rule = ModerationRuleReference.Create(scope.Id, "native-urgency", 1);
        var reason = ModerationRuleReference.Create(scope.Id, "native-urgency-reason", 1);
        var evidence = urgent
            ? ModerationFindingEvidenceReference.QualifiedUrgency(
                Guid.CreateVersion7(), Guid.CreateVersion7(), allegation.Id, 1, Now.AddMinutes(-1))
            : ModerationFindingEvidenceReference.Response(
                Guid.CreateVersion7(), allegation.Id, 1, Now.AddMinutes(-1));
        var finding = ModerationFinding.Finalize(Guid.CreateVersion7(), allegation, 1,
            allegation.Scope, null, null, rule, rule,
            reason, reason, adjudicator, evidence, "Independent qualified finding.", Now);
        var decision = ModerationDecision.Propose(Guid.CreateVersion7(), allegation.Id, subject, issuer,
            instance, scope, rule, [ModerationCapability.Publish],
            Now, Now.AddHours(1), null, 1, Now.AddMinutes(-1));
        return (decision, finding);
    }
}
