using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public class ModerationFindingAuthorityInvariantTests
{
    private static readonly DateTimeOffset OpenedAt = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FinalizedAt = OpenedAt.AddHours(1);
    private static readonly DateTimeOffset ReversedAt = FinalizedAt.AddHours(1);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FinalizationRetainsQualifiedCaseRuleAndPurposeSpecificEvidence(bool urgent)
    {
        var allegation = Case();
        var adjudicator = Human(allegation.Scope);
        var rule = Rule();
        var evidence = Evidence(allegation, urgent);
        var finding = Finalize(allegation, rule, adjudicator, evidence);

        finding.ValidatePersistedState();
        await Assert.That(finding.CaseId).IsEqualTo(allegation.Id);
        await Assert.That(finding.CaseRevision).IsEqualTo(1L);
        await Assert.That(finding.CaseOccurrenceId).IsEqualTo(allegation.OccurrenceId);
        await Assert.That(finding.Subject).IsEqualTo(allegation.Subject);
        await Assert.That(finding.Scope).IsEqualTo(allegation.Scope);
        await Assert.That(finding.Rule).IsEqualTo(rule);
        await Assert.That(finding.Adjudicator).IsEqualTo(adjudicator);
        await Assert.That(finding.Evidence).IsEqualTo(evidence);
        await Assert.That(finding.State).IsEqualTo(ModerationFindingState.Final);
        await Assert.That(finding.Revision).IsEqualTo(1L);
        await Assert.That(finding.FinalizedAt).IsEqualTo(FinalizedAt);
        await Assert.That(finding.UpdatedAt).IsEqualTo(FinalizedAt);
        await Assert.That(finding.ReversalReferenceId).IsNull();
        await Assert.That(allegation.State).IsEqualTo(ModerationCaseState.Open);
        await Assert.That(allegation.Revision).IsEqualTo(1L);
    }

    [Test]
    [Arguments(0L)]
    [Arguments(-1L)]
    [Arguments(2L)]
    public async Task StaleCaseRevisionCannotCreateFinding(long expectedRevision)
    {
        var allegation = Case();
        await Assert.That(() => ModerationFinding.Finalize(Guid.CreateVersion7(), allegation, expectedRevision,
            Rule(), Rule(), Human(allegation.Scope), Evidence(allegation), "Established violation.", FinalizedAt))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ClosedCaseCannotBecomeFinalAuthority()
    {
        var allegation = Case();
        var evidence = Evidence(allegation);
        allegation.Close(1, FinalizedAt);
        var rule = Rule();

        await Assert.That(() => ModerationFinding.Finalize(Guid.CreateVersion7(), allegation, 2,
            rule, rule, Human(allegation.Scope), evidence, "Established violation.", FinalizedAt))
            .Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task AdoptedRuleMustMatchOwnerCodeAndRevisionExactly(int difference)
    {
        var allegation = Case();
        var rule = Rule();
        var adopted = ModerationRuleReference.Create(
            difference == 0 ? Guid.CreateVersion7() : rule.OwnerId,
            difference == 1 ? "Different.Rule" : rule.Code,
            difference == 2 ? 2 : rule.Revision);

        await Assert.That(() => ModerationFinding.Finalize(Guid.CreateVersion7(), allegation, 1,
            rule, adopted, Human(allegation.Scope), Evidence(allegation), "Established violation.", FinalizedAt))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task AdjudicatorMustBeIndependentCanonicalHumanInTrustDomain(int defect)
    {
        var allegation = Case();
        var adjudicator = defect switch
        {
            0 => SubjectReference.Create(ModerationSubjectKind.Human, allegation.Subject.Id, allegation.Scope),
            1 => SubjectReference.Create(ModerationSubjectKind.Service, Guid.CreateVersion7(), allegation.Scope),
            _ => Human(AuthorityScope.TrustDomain(Guid.CreateVersion7()))
        };

        await Assert.That(() => Finalize(allegation, Rule(), adjudicator, Evidence(allegation)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task SameSubjectIdAtDifferentOriginDoesNotEvadeIndependence()
    {
        var allegation = Case();
        var adjudicator = SubjectReference.Create(ModerationSubjectKind.Human,
            allegation.Subject.Id, allegation.Scope.Parent!);

        await Assert.That(() => Finalize(allegation, Rule(), adjudicator, Evidence(allegation)))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EvidenceCannotBeReusedAcrossCaseOrRevision(bool wrongRevision)
    {
        var allegation = Case();
        var evidence = ModerationFindingEvidenceReference.Response(Guid.CreateVersion7(),
            wrongRevision ? allegation.Id : Guid.CreateVersion7(), wrongRevision ? 2 : 1, OpenedAt);

        await Assert.That(() => Finalize(allegation, Rule(), Human(allegation.Scope), evidence))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(-1)]
    [Arguments(61)]
    public async Task EvidenceCannotPrecedeCaseOrFollowFinalization(int minutes)
    {
        var allegation = Case();
        var evidence = ModerationFindingEvidenceReference.Response(
            Guid.CreateVersion7(), allegation.Id, 1, OpenedAt.AddMinutes(minutes));
        await Assert.That(() => Finalize(allegation, Rule(), Human(allegation.Scope), evidence))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task UrgencyRequiresDistinctNonemptyQualificationReference()
    {
        var allegation = Case();
        var id = Guid.CreateVersion7();
        await Assert.That(() => ModerationFindingEvidenceReference.QualifiedUrgency(
            id, Guid.Empty, allegation.Id, 1, OpenedAt)).Throws<ArgumentException>();
        await Assert.That(() => ModerationFindingEvidenceReference.QualifiedUrgency(
            id, id, allegation.Id, 1, OpenedAt)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task EvidenceRequiresIdentityCasePositiveRevisionAndTimestamp(int defect)
    {
        await Assert.That(() => ModerationFindingEvidenceReference.Response(
            defect == 0 ? Guid.Empty : Guid.CreateVersion7(),
            defect == 1 ? Guid.Empty : Guid.CreateVersion7(),
            defect == 2 ? 0 : 1,
            defect == 3 ? default : OpenedAt)).Throws<ArgumentException>();
    }

    [Test]
    public async Task FinalizationRequiresStableIdentityAndSubstantiveBoundedJustification()
    {
        var allegation = Case();
        var rule = Rule();
        await Assert.That(() => ModerationFinding.Finalize(Guid.Empty, allegation, 1, rule, rule,
            Human(allegation.Scope), Evidence(allegation), "Established violation.", FinalizedAt))
            .Throws<ArgumentException>();
        foreach (var justification in new[] { "", "  ", new string('x', 4001) })
        {
            await Assert.That(() => ModerationFinding.Finalize(Guid.CreateVersion7(), allegation, 1, rule, rule,
                Human(allegation.Scope), Evidence(allegation), justification, FinalizedAt))
                .Throws<ArgumentException>();
        }
    }

    [Test]
    [Arguments("12345678-1234-4234-8234-123456789abc")]
    [Arguments("12345678-1234-7234-0234-123456789abc")]
    public async Task FindingIdentityMustBeRfc4122VersionSeven(string value)
    {
        var allegation = Case();
        var rule = Rule();
        await Assert.That(() => ModerationFinding.Finalize(Guid.Parse(value), allegation, 1, rule, rule,
            Human(allegation.Scope), Evidence(allegation), "Established violation.", FinalizedAt))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ReversalPreservesFinalOwnershipJustificationAndTimePermanently()
    {
        var allegation = Case();
        var rule = Rule();
        var adjudicator = Human(allegation.Scope);
        var evidence = Evidence(allegation, true);
        var finding = Finalize(allegation, rule, adjudicator, evidence);
        var reverser = Human(allegation.Scope);
        var reference = Guid.CreateVersion7();
        allegation.Close(1, ReversedAt);

        finding.Reverse(reference, reverser, "Finding overturned on review.", 1, ReversedAt);
        finding.ValidatePersistedState();

        await Assert.That(finding.State).IsEqualTo(ModerationFindingState.Reversed);
        await Assert.That(finding.Revision).IsEqualTo(2L);
        await Assert.That(finding.ReversalReferenceId).IsEqualTo(reference);
        await Assert.That(finding.ReversedBy).IsEqualTo(reverser);
        await Assert.That(finding.ReversalJustification).IsEqualTo("Finding overturned on review.");
        await Assert.That(finding.ReversedAt).IsEqualTo(ReversedAt);
        await Assert.That(finding.UpdatedAt).IsEqualTo(ReversedAt);
        await Assert.That(finding.FinalizedAt).IsEqualTo(FinalizedAt);
        await Assert.That(finding.Justification).IsEqualTo("Established violation.");
        await Assert.That(finding.CaseId).IsEqualTo(allegation.Id);
        await Assert.That(finding.CaseRevision).IsEqualTo(1L);
        await Assert.That(finding.Subject).IsEqualTo(allegation.Subject);
        await Assert.That(finding.Scope).IsEqualTo(allegation.Scope);
        await Assert.That(finding.Rule).IsEqualTo(rule);
        await Assert.That(finding.Adjudicator).IsEqualTo(adjudicator);
        await Assert.That(finding.Evidence).IsEqualTo(evidence);
        await Assert.That(() => finding.Reverse(Guid.CreateVersion7(), adjudicator, "Another review.", 2,
            ReversedAt.AddHours(1))).Throws<InvalidOperationException>();
        await Assert.That(finding.ReversalReferenceId).IsEqualTo(reference);
        await Assert.That(finding.Revision).IsEqualTo(2L);
    }

    [Test]
    [Arguments(0L)]
    [Arguments(-1L)]
    [Arguments(2L)]
    public async Task StaleReversalLeavesFinalAuthorityUntouched(long expectedRevision)
    {
        var allegation = Case();
        var finding = Finalize(allegation, Rule(), Human(allegation.Scope), Evidence(allegation));
        await Assert.That(() => finding.Reverse(Guid.CreateVersion7(), Human(allegation.Scope),
            "Review.", expectedRevision, ReversedAt)).Throws<InvalidOperationException>();
        await Assert.That(finding.State).IsEqualTo(ModerationFindingState.Final);
        await Assert.That(finding.Revision).IsEqualTo(1L);
        await Assert.That(finding.ReversalReferenceId).IsNull();
        await Assert.That(finding.UpdatedAt).IsEqualTo(FinalizedAt);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task InvalidReversalCannotPartiallyWriteAudit(int defect)
    {
        var allegation = Case();
        var finding = Finalize(allegation, Rule(), Human(allegation.Scope), Evidence(allegation));
        var reverser = defect == 2 ? allegation.Subject : defect == 3
            ? Human(AuthorityScope.TrustDomain(Guid.CreateVersion7())) : Human(allegation.Scope);
        if (defect == 4)
        {
            await Assert.That(() => finding.Reverse(Guid.CreateVersion7(), reverser,
                "Review.", 1, FinalizedAt.AddTicks(-1))).Throws<InvalidOperationException>();
        }
        else
        {
            await Assert.That(() => finding.Reverse(defect == 0 ? Guid.Empty : Guid.CreateVersion7(),
                reverser, defect == 1 ? " " : "Review.", 1, ReversedAt)).Throws<ArgumentException>();
        }
        await Assert.That(finding.State).IsEqualTo(ModerationFindingState.Final);
        await Assert.That(finding.Revision).IsEqualTo(1L);
        await Assert.That(finding.ReversalReferenceId).IsNull();
        await Assert.That(finding.ReversedBy).IsNull();
        await Assert.That(finding.ReversedAt).IsNull();
        await Assert.That(finding.ReversalJustification).IsNull();
    }

    [Test]
    [Arguments("Revision", 0L)]
    [Arguments("Revision", 2L)]
    [Arguments("CaseRevision", 0L)]
    [Arguments("CaseRevision", 2L)]
    public async Task PersistedRevisionContradictionsFailBeforeMutation(string property, long value)
    {
        var allegation = Case();
        var finding = Finalize(allegation, Rule(), Human(allegation.Scope), Evidence(allegation));
        typeof(ModerationFinding).GetProperty(property)!.SetValue(finding, value);
        await Assert.That(finding.ValidatePersistedState).Throws<ArgumentException>();
        await Assert.That(() => finding.Reverse(Guid.CreateVersion7(), Human(allegation.Scope),
            "Review.", value, ReversedAt)).Throws<ArgumentException>();
        await Assert.That(finding.State).IsEqualTo(ModerationFindingState.Final);
        await Assert.That(finding.ReversalReferenceId).IsNull();
    }

    [Test]
    [Arguments("SubjectOriginSolutionId")]
    [Arguments("ScopeSolutionId")]
    [Arguments("AdjudicatorOriginSolutionId")]
    public async Task MissingPersistedAncestryCannotBecomeAuthority(string property)
    {
        var allegation = Case();
        var finding = Finalize(allegation, Rule(), Human(allegation.Scope), Evidence(allegation));
        typeof(ModerationFinding).GetProperty(property)!.SetValue(finding, null);
        await Assert.That(finding.ValidatePersistedState).Throws<ArgumentException>();
    }

    [Test]
    public async Task PurposeAndReversalAuditContradictionsAreRejectedAfterMaterialization()
    {
        var allegation = Case();
        var finding = Finalize(allegation, Rule(), Human(allegation.Scope), Evidence(allegation));
        typeof(ModerationFinding).GetProperty(nameof(ModerationFinding.UrgencyQualificationId))!
            .SetValue(finding, Guid.CreateVersion7());
        await Assert.That(finding.ValidatePersistedState).Throws<ArgumentException>();

        finding = Finalize(allegation, Rule(), Human(allegation.Scope), Evidence(allegation));
        typeof(ModerationFinding).GetProperty(nameof(ModerationFinding.ReversalReferenceId))!
            .SetValue(finding, Guid.CreateVersion7());
        await Assert.That(finding.ValidatePersistedState).Throws<ArgumentException>();

        finding = Finalize(allegation, Rule(), Human(allegation.Scope), Evidence(allegation));
        finding.Reverse(Guid.CreateVersion7(), Human(allegation.Scope), "Review.", 1, ReversedAt);
        typeof(ModerationFinding).GetProperty(nameof(ModerationFinding.ReversedById))!.SetValue(finding, null);
        await Assert.That(finding.ValidatePersistedState).Throws<ArgumentException>();
    }

    [Test]
    public async Task CorruptCaseCannotSeedOtherwisePlausibleFinding()
    {
        var allegation = Case();
        var evidence = Evidence(allegation);
        typeof(ModerationCase).GetProperty(nameof(ModerationCase.OccurrenceId))!.SetValue(allegation, Guid.Empty);
        await Assert.That(() => Finalize(allegation, Rule(), Human(allegation.Scope), evidence))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task PersistedEvidenceAndAuditMustRemainSemanticallyConsistent(int defect)
    {
        var allegation = Case();
        var finding = Finalize(allegation, Rule(), Human(allegation.Scope), Evidence(allegation, true));
        var (property, value) = defect switch
        {
            0 => (nameof(ModerationFinding.EvidencePurpose), (object)(ModerationFindingEvidencePurpose)99),
            1 => (nameof(ModerationFinding.EvidenceReferenceId), Guid.Empty),
            2 => (nameof(ModerationFinding.UrgencyQualificationId), Guid.Empty),
            3 => (nameof(ModerationFinding.EvidenceRecordedAt), FinalizedAt.AddTicks(1)),
            _ => (nameof(ModerationFinding.UpdatedAt), FinalizedAt.AddTicks(1))
        };
        typeof(ModerationFinding).GetProperty(property)!.SetValue(finding, value);
        await Assert.That(finding.ValidatePersistedState).Throws<ArgumentException>();
        await Assert.That(() => finding.Reverse(Guid.CreateVersion7(), Human(allegation.Scope),
            "Review.", 1, ReversedAt)).Throws<ArgumentException>();
        await Assert.That(finding.ReversalReferenceId).IsNull();
        await Assert.That(finding.State).IsEqualTo(ModerationFindingState.Final);
    }

    private static ModerationCase Case()
    {
        var trust = AuthorityScope.TrustDomain(Guid.CreateVersion7());
        var solution = AuthorityScope.Solution(trust, Guid.CreateVersion7());
        var instance = AuthorityScope.Instance(solution, Guid.CreateVersion7());
        var tenant = AuthorityScope.Tenant(instance, Guid.CreateVersion7());
        var community = AuthorityScope.Community(tenant, Guid.CreateVersion7());
        return ModerationCase.Open(Guid.CreateVersion7(), Human(community),
            tenant, Guid.CreateVersion7(), OpenedAt);
    }

    private static SubjectReference Human(AuthorityScope scope) =>
        SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope);

    private static ModerationRuleReference Rule() =>
        ModerationRuleReference.Create(Guid.CreateVersion7(), "Established.Rule", 1);

    private static ModerationFindingEvidenceReference Evidence(ModerationCase allegation, bool urgent = false) =>
        urgent
            ? ModerationFindingEvidenceReference.QualifiedUrgency(Guid.CreateVersion7(),
                Guid.CreateVersion7(), allegation.Id, allegation.Revision, OpenedAt)
            : ModerationFindingEvidenceReference.Response(Guid.CreateVersion7(),
                allegation.Id, allegation.Revision, OpenedAt);

    private static ModerationFinding Finalize(ModerationCase allegation, ModerationRuleReference rule,
        SubjectReference adjudicator, ModerationFindingEvidenceReference evidence) =>
        ModerationFinding.Finalize(Guid.CreateVersion7(), allegation, 1, rule,
            ModerationRuleReference.Create(rule.OwnerId, rule.Code, rule.Revision),
            adjudicator, evidence, "Established violation.", FinalizedAt);
}
