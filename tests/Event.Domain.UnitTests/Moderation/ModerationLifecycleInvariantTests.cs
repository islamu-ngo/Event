using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public class ModerationLifecycleInvariantTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task TenantAuthorityCannotCreateSolutionExclusion()
    {
        var instance = CreateInstance();
        var tenant = AuthorityScope.Tenant(instance, Guid.CreateVersion7());

        await Assert.ThrowsAsync<ArgumentException>(() =>
        {
            Propose(tenant, instance.Parent!, Now, Now.AddHours(1));
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task ScopeContainmentPreservesTenantAndSolutionIndependence()
    {
        var instance = CreateInstance();
        var tenant = AuthorityScope.Tenant(instance, Guid.CreateVersion7());
        var sibling = AuthorityScope.Tenant(instance, Guid.CreateVersion7());
        var community = AuthorityScope.Community(tenant, Guid.CreateVersion7());
        var otherSolution = AuthorityScope.Solution(instance.Parent!.Parent!, Guid.CreateVersion7());

        await Assert.That(tenant.Contains(community)).IsTrue();
        await Assert.That(tenant.Contains(sibling)).IsFalse();
        await Assert.That(tenant.Contains(otherSolution)).IsFalse();
        await Assert.That(community.Contains(tenant)).IsFalse();
    }

    [Test]
    public async Task MalformedScopeCannotSkipItsRequiredParent()
    {
        var trust = AuthorityScope.TrustDomain(Guid.CreateVersion7());

        await Assert.ThrowsAsync<ArgumentException>(() =>
        {
            AuthorityScope.Tenant(trust, Guid.CreateVersion7());
            return Task.CompletedTask;
        });
        await Assert.ThrowsAsync<ArgumentException>(() =>
        {
            AuthorityScope.TrustDomain(Guid.Empty);
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task SameIdentifiersUnderDifferentRootsAreNotTheSameJurisdiction()
    {
        var instanceId = Guid.CreateVersion7();
        var solutionId = Guid.CreateVersion7();
        var first = AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), solutionId), instanceId);
        var second = AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), solutionId), instanceId);

        await Assert.That(first.Contains(second)).IsFalse();
        await Assert.That(first == second).IsFalse();
    }

    [Test]
    public async Task AllegationAloneCannotProduceAnExecutableDecision()
    {
        var instance = CreateInstance();
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance);
        var allegation = ModerationCase.Open(Guid.CreateVersion7(), subject, instance, Guid.CreateVersion7(), Now);
        var decision = Propose(instance, instance, Now, Now.AddHours(1));

        await Assert.That(allegation.State).IsEqualTo(ModerationCaseState.Open);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            decision.Activate(Guid.CreateVersion7(), decision.Revision, Now);
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task SameControllerCannotSupplyTwoIndependentApprovals()
    {
        var instance = CreateInstance();
        var decision = Propose(instance, instance, Now, Now.AddHours(1), requiredApprovals: 2);
        var reviewer = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance);
        QualifyFinding(decision);
        decision.Approve(reviewer, instance, decision.Revision, Now);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            decision.Approve(reviewer, instance, decision.Revision, Now);
            return Task.CompletedTask;
        });
        await Assert.That(decision.State).IsEqualTo(ModerationDecisionState.AwaitingApproval);
        await Assert.That(decision.Approvals.Count).IsEqualTo(1);
    }

    [Test]
    public async Task IssuerAndSubjectCannotApproveTheirOwnSevereDecision()
    {
        var instance = CreateInstance();
        var decision = Propose(instance, instance, Now, Now.AddHours(1), requiredApprovals: 2);
        QualifyFinding(decision);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            decision.Approve(decision.Issuer, instance, decision.Revision, Now);
            return Task.CompletedTask;
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            decision.Approve(decision.Subject, instance, decision.Revision, Now);
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task ReviewerFromUnrelatedTenantCannotApprove()
    {
        var instance = CreateInstance();
        var target = AuthorityScope.Tenant(instance, Guid.CreateVersion7());
        var unrelated = AuthorityScope.Tenant(instance, Guid.CreateVersion7());
        var decision = Propose(target, target, Now, Now.AddHours(1));
        QualifyFinding(decision);
        var reviewer = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            decision.Approve(reviewer, unrelated, decision.Revision, Now);
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task IndefiniteDecisionRequiresReviewAndIndependentQuorum()
    {
        var instance = CreateInstance();

        await Assert.ThrowsAsync<ArgumentException>(() =>
        {
            Propose(instance, instance, Now, null);
            return Task.CompletedTask;
        });
        await Assert.ThrowsAsync<ArgumentException>(() =>
        {
            Propose(instance, instance, Now, null, reviewAt: Now.AddDays(7));
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task ScheduledContributionUsesExactHalfOpenInterval()
    {
        var instance = CreateInstance();
        var start = Now.AddHours(1);
        var end = Now.AddHours(2);
        var sanction = Activate(Propose(instance, instance, start, end), Now);

        await Assert.That(sanction.IsEffectiveAt(start.AddTicks(-1))).IsFalse();
        await Assert.That(sanction.IsEffectiveAt(start)).IsTrue();
        await Assert.That(sanction.IsEffectiveAt(end.AddTicks(-1))).IsTrue();
        await Assert.That(sanction.IsEffectiveAt(end)).IsFalse();
        await Assert.That(sanction.EndsAt).IsEqualTo(end);
    }

    [Test]
    public async Task ReversingOneContributionDoesNotLiftAnOverlappingRestriction()
    {
        var instance = CreateInstance();
        var firstDecision = Propose(instance, instance, Now, Now.AddDays(1));
        var secondDecision = Propose(instance, instance, Now, Now.AddDays(2), subject: firstDecision.Subject);
        var first = Activate(firstDecision, Now);
        var second = Activate(secondDecision, Now);
        first.Revoke(first.Revision, Now.AddMinutes(1));
        var outcome = EffectiveRestrictions.Evaluate(
            first.Subject, instance, ModerationCapability.Publish, Now.AddMinutes(2),
            [first, second], [], ModerationAuthorityState.Known);

        await Assert.That(outcome.Outcome).IsEqualTo(ModerationRestrictionOutcome.Denied);
        await Assert.That(outcome.ContributionIds.Count).IsEqualTo(1);
        await Assert.That(outcome.ContributionIds[0]).IsEqualTo(second.Id);
    }

    [Test]
    public async Task PublicationRestrictionDoesNotInferAdmissionOrCompanionEffects()
    {
        var instance = CreateInstance();
        var sanction = Activate(Propose(instance, instance, Now, Now.AddDays(1)), Now);
        var companion = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance);
        var admission = EffectiveRestrictions.Evaluate(
            sanction.Subject, instance, ModerationCapability.Admission, Now,
            [sanction], [], ModerationAuthorityState.Known);
        var companionPublication = EffectiveRestrictions.Evaluate(
            companion, instance, ModerationCapability.Publish, Now,
            [sanction], [], ModerationAuthorityState.Known);

        await Assert.That(admission.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unrestricted);
        await Assert.That(companionPublication.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unrestricted);
    }

    [Test]
    public async Task MutableInputCannotWidenPublishedDecisionCapabilities()
    {
        var instance = CreateInstance();
        var capabilities = new List<ModerationCapability> { ModerationCapability.Publish };
        var decision = Propose(instance, instance, Now, Now.AddDays(1), capabilities: capabilities);
        capabilities.Add(ModerationCapability.Admission);
        var sanction = Activate(decision, Now);

        await Assert.That(sanction.Capabilities.Contains(ModerationCapability.Admission)).IsFalse();
        await Assert.That(sanction.Capabilities.Count).IsEqualTo(1);
    }

    [Test]
    public async Task StaleRevisionCannotReverseOrApproveCurrentState()
    {
        var instance = CreateInstance();
        var decision = Propose(instance, instance, Now, Now.AddDays(1));
        var oldRevision = decision.Revision;
        QualifyFinding(decision);
        var reviewer = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            decision.Approve(reviewer, instance, oldRevision, Now);
            return Task.CompletedTask;
        });
        var sanction = Activate(decision, Now);
        var sanctionRevision = sanction.Revision;
        sanction.Revoke(sanctionRevision, Now.AddMinutes(1));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            sanction.Revoke(sanctionRevision, Now.AddMinutes(2));
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task EmergencyProtectionExpiresWithoutBecomingPermanentDiscipline()
    {
        var instance = CreateInstance();
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance);
        var protector = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance);
        var hold = ModerationProtectiveMeasure.Create(
            Guid.CreateVersion7(), subject, protector, instance, instance,
            [ModerationCapability.Admission], "qualified-danger", Now, Now.AddHours(1), Now.AddMinutes(30));

        await Assert.That(hold.IsEffectiveAt(Now.AddHours(1))).IsFalse();
        await Assert.That(hold.IsReviewOverdueAt(Now.AddMinutes(30))).IsTrue();
        await Assert.That(hold.EndsAt).IsEqualTo(Now.AddHours(1));
    }

    private static AuthorityScope CreateInstance() =>
        AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
            Guid.CreateVersion7());

    private static ModerationDecision Propose(
        AuthorityScope issuerScope,
        AuthorityScope target,
        DateTimeOffset startsAt,
        DateTimeOffset? endsAt,
        int requiredApprovals = 1,
        DateTimeOffset? reviewAt = null,
        SubjectReference? subject = null,
        IEnumerable<ModerationCapability>? capabilities = null) =>
        ModerationDecision.Propose(
            Guid.CreateVersion7(), Guid.CreateVersion7(),
            subject ?? SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), issuerScope),
            SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), issuerScope),
            issuerScope, target, ModerationRuleReference.Create(target.TrustDomainId, "publication", 1),
            capabilities ?? [ModerationCapability.Publish], startsAt, endsAt, reviewAt, requiredApprovals, Now);

    private static void QualifyFinding(ModerationDecision decision)
    {
        decision.RecordResponse(decision.Revision, Now);
        decision.AcceptFinding(Guid.CreateVersion7(), decision.Revision, Now);
    }

    private static ModerationSanction Activate(ModerationDecision decision, DateTimeOffset now)
    {
        if (decision.State == ModerationDecisionState.Proposed)
        {
            QualifyFinding(decision);
        }

        for (var index = decision.Approvals.Count; index < decision.RequiredApprovals; index++)
        {
            var reviewer = SubjectReference.Create(
                ModerationSubjectKind.Human, Guid.CreateVersion7(), decision.IssuingScope);
            decision.Approve(reviewer, decision.IssuingScope, decision.Revision, now);
        }

        return decision.Activate(Guid.CreateVersion7(), decision.Revision, now);
    }
}
