using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public class ModerationWorstBreakInvariantTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(ModerationAuthorityState.Unknown)]
    [Arguments(ModerationAuthorityState.Pending)]
    [Arguments(ModerationAuthorityState.Unavailable)]
    public async Task UnsettledErasedSubjectAuthorityCannotPermitAdmissionOrPrivateLogistics(
        ModerationAuthorityState authority)
    {
        var scope = CreateInstance();
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope);

        foreach (var capability in new[] { ModerationCapability.Admission, ModerationCapability.PrivateLogistics })
        {
            var result = EffectiveRestrictions.Evaluate(
                subject, scope, capability, Now, [], [], authority);
            await Assert.That(result.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unavailable);
        }
    }

    [Test]
    [Arguments(ModerationCapability.ProtectedStatus)]
    [Arguments(ModerationCapability.Contestation)]
    [Arguments(ModerationCapability.Support)]
    [Arguments(ModerationCapability.PrivacyErasure)]
    public async Task PendingAuthorityPreservesSeparatelyAuthorizedProtectedRemedies(
        ModerationCapability capability)
    {
        var scope = CreateInstance();
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope);
        var result = EffectiveRestrictions.Evaluate(
            subject, scope, capability, Now, [], [], ModerationAuthorityState.Pending);

        await Assert.That(result.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unrestricted);
    }

    [Test]
    public async Task KnownAdmissionExclusionRetainsExactScopeAndOriginalEnd()
    {
        var instance = CreateInstance();
        var eventScope = AuthorityScope.Resource(
            AuthorityScope.Tenant(instance, Guid.CreateVersion7()), Guid.CreateVersion7());
        var unrelated = AuthorityScope.Resource(eventScope.Parent!, Guid.CreateVersion7());
        var end = Now.AddDays(2);
        var sanction = AdmissionExclusion(eventScope, end);
        var denied = EffectiveRestrictions.Evaluate(
            sanction.Subject, eventScope, ModerationCapability.Admission, Now,
            [sanction], [], ModerationAuthorityState.Known);
        var otherVenue = EffectiveRestrictions.Evaluate(
            sanction.Subject, unrelated, ModerationCapability.Admission, Now,
            [sanction], [], ModerationAuthorityState.Known);
        var expired = EffectiveRestrictions.Evaluate(
            sanction.Subject, eventScope, ModerationCapability.Admission, end,
            [sanction], [], ModerationAuthorityState.Known);

        await Assert.That(denied.Outcome).IsEqualTo(ModerationRestrictionOutcome.Denied);
        await Assert.That(otherVenue.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unrestricted);
        await Assert.That(expired.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unrestricted);
        await Assert.That(sanction.Scope).IsEqualTo(eventScope);
        await Assert.That(sanction.EndsAt).IsEqualTo(end);
    }

    [Test]
    public async Task AdmissionExclusionDoesNotBlockProtectedRemedyOrInventGlobalEnrollmentBan()
    {
        var instance = CreateInstance();
        var scope = AuthorityScope.Resource(
            AuthorityScope.Tenant(instance, Guid.CreateVersion7()), Guid.CreateVersion7());
        var sanction = AdmissionExclusion(scope, Now.AddDays(2));
        var remedy = EffectiveRestrictions.Evaluate(
            sanction.Subject, scope, ModerationCapability.Contestation, Now,
            [sanction], [], ModerationAuthorityState.Known);
        var enrollment = EffectiveRestrictions.Evaluate(
            sanction.Subject, instance, ModerationCapability.Enrollment, Now,
            [sanction], [], ModerationAuthorityState.Known);

        await Assert.That(remedy.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unrestricted);
        await Assert.That(enrollment.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unrestricted);
    }

    [Test]
    public async Task ReversalDoesNotRestoreUnrelatedRestrictionsOrExtendTheOriginalInterval()
    {
        var scope = CreateInstance();
        var end = Now.AddDays(2);
        var sanction = AdmissionExclusion(scope, end);
        sanction.Revoke(sanction.Revision, Now.AddHours(1));
        var result = EffectiveRestrictions.Evaluate(
            sanction.Subject, scope, ModerationCapability.PrivateLogistics, Now.AddHours(2),
            [sanction], [], ModerationAuthorityState.Known);

        await Assert.That(result.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unrestricted);
        await Assert.That(sanction.EndsAt).IsEqualTo(end);
        await Assert.That(sanction.State).IsEqualTo(ModerationSanctionState.Revoked);
    }

    private static AuthorityScope CreateInstance() =>
        AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
            Guid.CreateVersion7());

    private static ModerationSanction AdmissionExclusion(AuthorityScope scope, DateTimeOffset end)
    {
        var decision = ModerationDecision.Propose(
            Guid.CreateVersion7(), Guid.CreateVersion7(),
            SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope),
            SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope),
            scope, scope, ModerationRuleReference.Create(scope.TrustDomainId, "event-danger", 1),
            [ModerationCapability.Admission, ModerationCapability.PrivateLogistics],
            Now, end, null, 1, Now);
        decision.RecordResponse(decision.Revision, Now);
        decision.AcceptFinding(Guid.CreateVersion7(), decision.Revision, Now);
        decision.Approve(
            SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope),
            scope, decision.Revision, Now);
        return decision.Activate(Guid.CreateVersion7(), decision.Revision, Now);
    }
}
