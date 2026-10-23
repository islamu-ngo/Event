using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public class ModerationAttributionBreakerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task SharedMailboxDoesNotExtendFindingToAnotherCanonicalHuman()
    {
        var first = MailboxUser();
        var second = MailboxUser();
        var scope = Scope();
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, first.Id, scope);
        var other = SubjectReference.Create(ModerationSubjectKind.Human, second.Id, scope);
        var allegation = ModerationCase.Open(Guid.CreateVersion7(), subject,
            scope, Guid.CreateVersion7(), Now);

        var finding = Finalize(allegation);
        finding.ValidatePersistedState();

        await Assert.That(first.Email).IsEqualTo(second.Email);
        await Assert.That(finding.Subject).IsEqualTo(subject);
        await Assert.That(finding.Subject == other).IsFalse();
        await Assert.That(finding.Scope).IsEqualTo(scope);
        await Assert.That(finding.Scope.Kind).IsEqualTo(ModerationScopeKind.Tenant);
        await Assert.That(allegation.Subject).IsEqualTo(subject);
    }

    [Test]
    public async Task OrganizerControlledParticipantTargetCannotEstablishFinalGuilt()
    {
        var scope = Scope();
        var evidence = Evidence(scope, ModerationCaseTargetKind.ParticipantIncident);
        var allegation = ModerationCase.OpenAllegation(Guid.CreateVersion7(), scope, evidence, Now);

        await Assert.That(() => Finalize(allegation)).Throws<InvalidOperationException>();
        await Assert.That(() => ModerationReviewerAssignment.Assign(Guid.CreateVersion7(),
            allegation, 1, Guid.CreateVersion7(), Guid.CreateVersion7(), Now)).Throws<ArgumentException>();
        allegation.ValidatePersistedState();
        await Assert.That(allegation.Subject).IsNull();
        await Assert.That(allegation.Target).IsEqualTo(evidence.Target);
        await Assert.That(allegation.Scope).IsEqualTo(scope);
        await Assert.That(allegation.State).IsEqualTo(ModerationCaseState.Open);
        await Assert.That(allegation.Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task CopiedReportCannotMultiplyMaterialFactsOrBecomeFinalGuilt()
    {
        var scope = Scope();
        var original = Evidence(scope, ModerationCaseTargetKind.LocalIncident);
        var allegation = ModerationCase.OpenAllegation(Guid.CreateVersion7(), scope, original, Now);
        var copy = ModerationEvidenceReference.Create(original.Target, original.SourceOrigin,
            original.OccurrenceId, original.SourceId, original.SourceVersion, original.MaterialFactId);

        allegation.RecordEvidence(copy, 1, Now.AddMinutes(1));
        allegation.RecordEvidence(copy, 1, Now.AddMinutes(2));

        await Assert.That(() => Finalize(allegation)).Throws<InvalidOperationException>();
        allegation.ValidatePersistedState();
        await Assert.That(allegation.Evidence.Count).IsEqualTo(1);
        await Assert.That(allegation.Evidence.Single().ToReference(original.Target, original.OccurrenceId))
            .IsEqualTo(original);
        await Assert.That(allegation.Revision).IsEqualTo(1L);
        await Assert.That(allegation.UpdatedAt).IsEqualTo(Now);
        await Assert.That(allegation.Subject).IsNull();
        await Assert.That(allegation.Scope).IsEqualTo(scope);
    }

    [Test]
    [Arguments(ModerationScopeKind.TrustDomain)]
    [Arguments(ModerationScopeKind.Solution)]
    [Arguments(ModerationScopeKind.Instance)]
    [Arguments(ModerationScopeKind.Tenant)]
    public async Task ForgedProviderSourceAncestryCannotBecomeCaseEvidence(ModerationScopeKind kind)
    {
        var scope = Scope();
        var original = Evidence(scope, ModerationCaseTargetKind.ParticipantIncident);
        var allegation = ModerationCase.OpenAllegation(Guid.CreateVersion7(), scope, original, Now);
        var instance = scope.Parent!;
        var solution = instance.Parent!;
        var forged = kind switch
        {
            ModerationScopeKind.TrustDomain => solution.Parent!,
            ModerationScopeKind.Solution => solution,
            ModerationScopeKind.Instance => AuthorityScope.Instance(solution, Guid.CreateVersion7()),
            _ => AuthorityScope.Tenant(instance, Guid.CreateVersion7())
        };

        await Assert.That(() => ModerationEvidenceReference.Create(original.Target, forged,
            original.OccurrenceId, original.SourceId, original.SourceVersion, original.MaterialFactId))
            .Throws<ArgumentException>();
        allegation.ValidatePersistedState();
        await Assert.That(allegation.Scope).IsEqualTo(scope);
        await Assert.That(allegation.Subject).IsNull();
        await Assert.That(allegation.Revision).IsEqualTo(1L);
        await Assert.That(allegation.Evidence.Count).IsEqualTo(1);
        await Assert.That(allegation.Evidence.Single().ToReference(original.Target, original.OccurrenceId))
            .IsEqualTo(original);
    }

    private static User MailboxUser()
    {
        var id = Guid.CreateVersion7();
        return new User
        {
            Id = id,
            Pii = new UserPii
            {
                UserId = id,
                Email = "shared@example.invalid",
                FirstName = "Fixture",
                LastName = "Human"
            }
        };
    }

    private static AuthorityScope Scope() => AuthorityScope.Tenant(
        AuthorityScope.Instance(AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()),
            Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());

    private static ModerationEvidenceReference Evidence(AuthorityScope scope, ModerationCaseTargetKind kind) =>
        ModerationEvidenceReference.Create(
            ModerationCaseTarget.Create(kind, Guid.CreateVersion7(), scope, Guid.CreateVersion7()), scope,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

    private static ModerationFinding Finalize(ModerationCase allegation)
    {
        var rule = ModerationRuleReference.Create(Guid.CreateVersion7(), "Attribution.Rule", 1);
        var reason = ModerationRuleReference.Create(Guid.CreateVersion7(), "Attribution.Reason", 1);
        var adjudicator = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), allegation.Scope);
        return ModerationFinding.Finalize(Guid.CreateVersion7(), allegation, allegation.Revision,
            allegation.Scope, allegation.AttributionSourceId, allegation.AttributionSourceId is null ? null : 1,
            rule, rule, reason, reason, adjudicator,
            ModerationFindingEvidenceReference.Response(Guid.CreateVersion7(), allegation.Id, allegation.Revision, Now),
            "The finding concerns only its explicitly attributed subject.", Now);
    }
}
