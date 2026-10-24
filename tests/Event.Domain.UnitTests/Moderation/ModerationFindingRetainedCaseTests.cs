using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationFindingRetainedCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task RetainedOwnershipPreservesTheProceedingAcrossClosureAndNarrowActionScope(
        bool resource, bool closed)
    {
        var owner = Owner();
        var scope = resource ? AuthorityScope.Resource(owner.Scope, Guid.CreateVersion7()) : owner.Scope;
        var finding = Finding(owner, scope);
        if (closed) owner.Close(owner.Revision, Now.AddSeconds(1));

        await Assert.That(finding.MatchesRetainedCase(owner)).IsTrue();
        await Assert.That(finding.CaseRevision).IsEqualTo(1L);
        await Assert.That(owner.Revision).IsEqualTo(closed ? 2L : 1L);
        await Assert.That(finding.Scope).IsEqualTo(scope);
        await Assert.That(finding.AttributionGeneration).IsEqualTo(2L);
        await Assert.That(owner.AttributionSourceId == finding.AttributionSourceId).IsFalse();
    }

    [Test]
    [Arguments("case")]
    [Arguments("subject")]
    [Arguments("occurrence")]
    [Arguments("creation")]
    [Arguments("attribution")]
    public async Task ValidlyShapedFindingCannotBorrowAnotherRetainedOwnershipCoordinate(string coordinate)
    {
        var owner = Owner();
        var finding = Finding(owner, owner.Scope);
        switch (coordinate)
        {
            case "case":
                Set(finding, nameof(finding.CaseId), Guid.CreateVersion7());
                break;
            case "subject":
                Set(finding, nameof(finding.SubjectId), Guid.CreateVersion7());
                break;
            case "occurrence":
                Set(finding, nameof(finding.CaseOccurrenceId), Guid.CreateVersion7());
                break;
            case "creation":
                Set(finding, nameof(finding.CaseCreatedAt), Now.AddSeconds(-1));
                break;
            case "attribution":
                Set(finding, nameof(finding.AttributionSourceId), null);
                Set(finding, nameof(finding.AttributionGeneration), 0L);
                break;
            default:
                throw new ArgumentException("Unknown ownership coordinate.", nameof(coordinate));
        }
        finding.ValidatePersistedState();
        await Assert.That(finding.MatchesRetainedCase(owner)).IsFalse();
        await Assert.That(owner.Revision).IsEqualTo(1L);
        await Assert.That(owner.State).IsEqualTo(ModerationCaseState.Open);
    }

    [Test]
    public async Task MatchingCaseAncestryDoesNotSelectOneResourceForAnotherEffect()
    {
        var owner = Owner();
        var first = Finding(owner, AuthorityScope.Resource(owner.Scope, Guid.CreateVersion7()));
        var second = Finding(owner, AuthorityScope.Resource(owner.Scope, Guid.CreateVersion7()));

        await Assert.That(first.MatchesRetainedCase(owner)).IsTrue();
        await Assert.That(second.MatchesRetainedCase(owner)).IsTrue();
        await Assert.That(first.Scope == second.Scope).IsFalse();
    }

    private static void Set(ModerationFinding finding, string name, object? value) =>
        typeof(ModerationFinding).GetProperty(name)!.SetValue(finding, value);

    private static ModerationFinding Finding(ModerationCase owner, AuthorityScope scope)
    {
        var rule = ModerationRuleReference.Create(owner.Scope.Id, "retained-case", 1);
        return ModerationFinding.Finalize(Guid.CreateVersion7(), owner, owner.Revision, scope,
            Guid.CreateVersion7(), 2, rule, rule, rule, rule,
            SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), owner.Scope.Parent!),
            ModerationFindingEvidenceReference.Response(Guid.CreateVersion7(), owner.Id, owner.Revision, Now),
            "The qualified finding retains its original case ancestry.", Now);
    }

    private static ModerationCase Owner()
    {
        var instance = AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        var tenant = AuthorityScope.Tenant(instance, Guid.CreateVersion7());
        var subject = SubjectReference.Create(ModerationSubjectKind.Content, Guid.CreateVersion7(), tenant);
        var evidence = ModerationEvidenceReference.Create(ModerationCaseTarget.Create(
            ModerationCaseTargetKind.EventVersion, subject.Id, tenant, Guid.CreateVersion7()),
            tenant, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var occurrence = ModerationCase.OpenAllegation(Guid.CreateVersion7(), tenant, evidence, Now);
        return ModerationCase.OpenAttributed(Guid.CreateVersion7(), subject, occurrence, 1, Guid.CreateVersion7(), Now);
    }
}
