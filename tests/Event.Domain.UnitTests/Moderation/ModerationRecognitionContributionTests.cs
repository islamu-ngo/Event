using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationRecognitionContributionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(ModerationReferenceRecognition.SeparatelyQualifiedFinalAction)]
    [Arguments(ModerationReferenceRecognition.SeparatelyQualifiedFinalEnrollment)]
    public async Task Recognition_separates_action_from_admission_without_widening_original_rights(
        ModerationReferenceRecognition purpose)
    {
        var (decision, sanction) = FinalContribution(
            [ModerationCapability.Publish, ModerationCapability.Admission,
                ModerationCapability.PrivateLogistics, ModerationCapability.Enrollment]);
        var retained = ModerationRecognitionContribution.Capture(
            decision, sanction, purpose, Now, Now.AddDays(3));
        var expected = purpose == ModerationReferenceRecognition.SeparatelyQualifiedFinalAction
            ? new[] { ModerationCapability.Publish }
            : new[] { ModerationCapability.Admission, ModerationCapability.PrivateLogistics, ModerationCapability.Enrollment };
        await Assert.That(retained.Capabilities.ToArray()).IsEquivalentTo(expected);
        await Assert.That(retained.Scope).IsEqualTo(sanction.Scope);
        await Assert.That(retained.EndsAt).IsEqualTo(Now.AddDays(2));
        await Assert.That(retained.MatchesCurrent(decision, sanction, Now)).IsTrue();
        await Assert.That(retained.MatchesCurrent(decision, sanction, Now.AddDays(2))).IsFalse();
        await Assert.That(retained.Scope.Contains(sanction.Scope.Parent!)).IsFalse();
    }

    [Test]
    [Arguments("never")]
    [Arguments("future")]
    [Arguments("expired")]
    [Arguments("revoked")]
    [Arguments("unrelated-purpose")]
    [Arguments("retention")]
    public async Task Recognition_rejects_unjustified_or_noncurrent_contributions(string defect)
    {
        var start = defect == "future" ? Now.AddHours(1) : Now;
        var (decision, sanction) = FinalContribution([ModerationCapability.Publish], start);
        if (defect == "revoked") sanction.Revoke(1, Now);
        var purpose = defect == "never" ? ModerationReferenceRecognition.Never
            : defect == "unrelated-purpose" ? ModerationReferenceRecognition.SeparatelyQualifiedFinalEnrollment
            : ModerationReferenceRecognition.SeparatelyQualifiedFinalAction;
        var captureAt = defect == "expired" ? Now.AddDays(2) : Now;
        await Assert.That(() => ModerationRecognitionContribution.Capture(
            decision, sanction, purpose, captureAt, defect == "retention" ? Now : Now.AddDays(3)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Reversal_and_supersession_defeat_retained_material_without_restoring_other_rights()
    {
        var (decision, sanction) = FinalContribution([ModerationCapability.Publish, ModerationCapability.Admission]);
        var retained = ModerationRecognitionContribution.Capture(decision, sanction,
            ModerationReferenceRecognition.SeparatelyQualifiedFinalEnrollment, Now, Now.AddDays(3));
        sanction.Revoke(1, Now.AddHours(1));
        await Assert.That(retained.MatchesCurrent(decision, sanction, Now.AddHours(1))).IsFalse();
        await Assert.That(retained.EndsAt).IsEqualTo(Now.AddDays(2));
        var (otherDecision, other) = FinalContribution([ModerationCapability.Admission]);
        await Assert.That(retained.MatchesCurrent(otherDecision, other, Now)).IsFalse();
        var otherRetained = ModerationRecognitionContribution.Capture(otherDecision, other,
            ModerationReferenceRecognition.SeparatelyQualifiedFinalEnrollment, Now, Now.AddDays(3));
        other.Supersede(Guid.CreateVersion7(), 1, Now.AddHours(1));
        await Assert.That(otherRetained.MatchesCurrent(otherDecision, other, Now.AddHours(1))).IsFalse();
    }

    [Test]
    public async Task A_retention_deadline_cannot_extend_enforcement_or_preserve_expired_matching_material()
    {
        var (decision, sanction) = FinalContribution([ModerationCapability.Admission]);
        var retained = ModerationRecognitionContribution.Capture(decision, sanction,
            ModerationReferenceRecognition.SeparatelyQualifiedFinalEnrollment, Now, Now.AddHours(1));
        await Assert.That(retained.MatchesCurrent(decision, sanction, Now.AddMinutes(59))).IsTrue();
        await Assert.That(retained.MatchesCurrent(decision, sanction, Now.AddHours(1))).IsFalse();
        await Assert.That(sanction.IsEffectiveAt(Now.AddHours(1))).IsTrue();
        await Assert.That(sanction.EndsAt).IsEqualTo(Now.AddDays(2));
    }

    [Test]
    public async Task Recognition_cannot_capture_a_service_or_substitute_another_final_decision()
    {
        var (serviceDecision, serviceSanction) = FinalContribution([ModerationCapability.Publish],
            subjectKind: ModerationSubjectKind.Service);
        await Assert.That(() => ModerationRecognitionContribution.Capture(serviceDecision, serviceSanction,
            ModerationReferenceRecognition.SeparatelyQualifiedFinalAction, Now, Now.AddDays(3)))
            .Throws<ArgumentException>();
        var (decision, sanction) = FinalContribution([ModerationCapability.Publish]);
        var (otherDecision, _) = FinalContribution([ModerationCapability.Publish]);
        await Assert.That(() => ModerationRecognitionContribution.Capture(otherDecision, sanction,
            ModerationReferenceRecognition.SeparatelyQualifiedFinalAction, Now, Now.AddDays(3)))
            .Throws<ArgumentException>();
        await Assert.That(decision.Id).IsNotEqualTo(otherDecision.Id);
    }

    private static (ModerationDecision Decision, ModerationSanction Sanction) FinalContribution(
        IEnumerable<ModerationCapability> capabilities, DateTimeOffset? startsAt = null,
        ModerationSubjectKind subjectKind = ModerationSubjectKind.Human)
    {
        var instance = AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        var scope = AuthorityScope.Resource(AuthorityScope.Tenant(instance, Guid.CreateVersion7()), Guid.CreateVersion7());
        var decision = ModerationDecision.Propose(Guid.CreateVersion7(), Guid.CreateVersion7(),
            SubjectReference.Create(subjectKind, Guid.CreateVersion7(), instance),
            SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance),
            instance, scope, ModerationRuleReference.Create(scope.Id, "recognition.fixture", 1),
            capabilities, startsAt ?? Now, Now.AddDays(2), null, 1, Now);
        decision.RecordResponse(1, Now);
        decision.AcceptFinding(Guid.CreateVersion7(), 2, Now);
        decision.Approve(SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance),
            instance, 3, Now);
        return (decision, decision.Activate(Guid.CreateVersion7(), 4, Now));
    }
}
