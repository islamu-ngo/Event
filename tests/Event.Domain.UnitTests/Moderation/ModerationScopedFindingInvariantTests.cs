using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationScopedFindingInvariantTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ScopedFindingsRetainOneTenantOwnedSubjectCaseAndExactAttributionGeneration()
    {
        var owner = Case();
        var firstScope = AuthorityScope.Resource(owner.Scope, Guid.CreateVersion7());
        var secondScope = AuthorityScope.Resource(owner.Scope, Guid.CreateVersion7());
        var source = Guid.CreateVersion7();
        var first = Finding(owner, firstScope, source, 2);
        var second = Finding(owner, secondScope, source, 2);

        first.ValidatePersistedState();
        second.ValidatePersistedState();
        await Assert.That(first.Scope).IsEqualTo(firstScope);
        await Assert.That(second.Scope).IsEqualTo(secondScope);
        await Assert.That(first.CaseId).IsEqualTo(second.CaseId);
        await Assert.That(first.CaseOccurrenceId).IsEqualTo(second.CaseOccurrenceId);
        await Assert.That(first.Subject).IsEqualTo(second.Subject);
        await Assert.That(first.AttributionSourceId).IsEqualTo(source);
        await Assert.That(first.AttributionGeneration).IsEqualTo(2L);
        await Assert.That(owner.Scope.Kind).IsEqualTo(ModerationScopeKind.Tenant);
        await Assert.That(owner.Revision).IsEqualTo(1L);
        await Assert.That(owner.AttributionSourceId).IsNotEqualTo(source);
    }

    [Test]
    [Arguments("wider")]
    [Arguments("foreign")]
    public async Task AttributedFindingCannotWidenOrLeaveItsOwningTenant(string defect)
    {
        var owner = Case();
        var scope = defect == "wider" ? owner.Scope.Parent!
            : AuthorityScope.Resource(AuthorityScope.Tenant(owner.Scope.Parent!, Guid.CreateVersion7()),
                Guid.CreateVersion7());

        await Assert.That(() => Finding(owner, scope, owner.AttributionSourceId, 1))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments("missing")]
    [Arguments("empty")]
    [Arguments("missing-generation")]
    [Arguments("zero")]
    public async Task AttributedFindingRequiresACompleteImmutableGenerationReference(string defect)
    {
        var owner = Case();
        Guid? source = defect == "missing" ? null
            : defect == "empty" ? Guid.Empty : owner.AttributionSourceId;
        long? generation = defect == "missing-generation" ? null : defect == "zero" ? 0 : 1;

        await Assert.That(() => Finding(owner, owner.Scope, source, generation))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task UnattributedSubjectCaseCannotClaimAnAttributionGeneration()
    {
        var attributed = Case();
        var owner = ModerationCase.Open(Guid.CreateVersion7(), attributed.Subject!, attributed.Scope,
            Guid.CreateVersion7(), Now);

        await Assert.That(() => Finding(owner, owner.Scope, Guid.CreateVersion7(), 1))
            .Throws<ArgumentException>();
        await Assert.That(Finding(owner, owner.Scope, null, null).AttributionSourceId).IsNull();
    }

    [Test]
    public async Task ReversalPreservesScopedGenerationAndDoesNotReverseAnotherScopedFinding()
    {
        var owner = Case();
        var source = Guid.CreateVersion7();
        var first = Finding(owner, AuthorityScope.Resource(owner.Scope, Guid.CreateVersion7()), source, 2);
        var second = Finding(owner, AuthorityScope.Resource(owner.Scope, Guid.CreateVersion7()), source, 2);

        first.Reverse(Guid.CreateVersion7(), first.Adjudicator,
            "Independent correction of this exact resource finding.", 1, Now.AddMinutes(1));

        await Assert.That(first.State).IsEqualTo(ModerationFindingState.Reversed);
        await Assert.That(first.AttributionSourceId).IsEqualTo(source);
        await Assert.That(first.AttributionGeneration).IsEqualTo(2L);
        await Assert.That(second.State).IsEqualTo(ModerationFindingState.Final);
        await Assert.That(owner.State).IsEqualTo(ModerationCaseState.Open);
        await Assert.That(owner.Revision).IsEqualTo(1L);
    }

    private static ModerationFinding Finding(
        ModerationCase owner, AuthorityScope scope, Guid? source, long? generation)
    {
        var rule = ModerationRuleReference.Create(owner.Scope.Id, "scoped-finding", 1);
        var reviewer = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), owner.Scope.Parent!);
        return ModerationFinding.Finalize(Guid.CreateVersion7(), owner, 1, scope, source, generation,
            rule, rule, rule, rule, reviewer,
            ModerationFindingEvidenceReference.Response(Guid.CreateVersion7(), owner.Id, 1, Now),
            "The fenced native writer qualifies only the selected resource.", Now);
    }

    private static ModerationCase Case()
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
