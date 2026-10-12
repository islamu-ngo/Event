using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationConstitutionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Adoption_BindsSubstanceAndGeneration_AndWithdrawalSurvivesExpiry()
    {
        var source = Source();
        var adoption = ModerationConstitutionAdoption.Adopt(Guid.CreateVersion7(), source,
            Guid.CreateVersion7(), 5, Now, Now.AddMinutes(4));
        await Assert.That(adoption.MatchesCurrent(source, Now)).IsTrue();
        await Assert.That(adoption.MatchesCurrent(Source(), Now)).IsFalse();
        await Assert.That(adoption.MatchesCurrent(source, Now.AddMinutes(4))).IsFalse();
        adoption.Withdraw(1, Guid.CreateVersion7(), Now.AddHours(1));
        await Assert.That(adoption.MatchesCurrent(source, Now)).IsFalse();
        await Assert.That(() => adoption.Withdraw(2, Guid.CreateVersion7(), Now.AddHours(2)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task Adoption_RejectsRecorderStaleGenerationOrExtendedLease(int change)
    {
        var source = Source();
        await Assert.That(() => ModerationConstitutionAdoption.Adopt(Guid.CreateVersion7(), source,
            change == 0 ? source.RecordedByHumanId : Guid.CreateVersion7(),
            change == 1 ? source.WriterGeneration : 5, Now,
            change == 2 ? source.ValidUntil.AddTicks(1) : Now.AddMinutes(4))).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task Constitution_RejectsMissingNormativeRequirements(int change)
    {
        var terms = Terms();
        terms = change switch
        {
            0 => terms with { Evidence = default },
            1 => terms with { Remedies = default },
            2 => terms with { Independence = default },
            3 => terms with { SupportRoute = "" },
            _ => terms with { EscalationAfter = TimeSpan.Zero }
        };
        await Assert.That(terms.Validate).Throws<ArgumentException>();
    }

    [Test]
    public async Task ReviewerAvailability_IsFiniteSelfOwnedAndTerminal()
    {
        var reviewer = Guid.CreateVersion7();
        var availability = ModerationConstitutionAvailability.Record(Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), reviewer,
            2, 3, Now, Now.AddMinutes(5));
        await Assert.That(availability.IsCurrent(Now)).IsTrue();
        await Assert.That(availability.IsCurrent(Now.AddMinutes(5))).IsFalse();
        availability.Withdraw(1, reviewer, Now.AddHours(1));
        await Assert.That(availability.IsCurrent(Now)).IsFalse();
        await Assert.That(() => availability.Withdraw(2, reviewer, Now.AddHours(2)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ReviewerRoster_RejectsSharedControllersAndMissingQualification()
    {
        var source = Source();
        var reviewer = source.Reviewers[0];
        await Assert.That(() => Source([
            reviewer.ToTerms(),
            reviewer.ToTerms() with { AvailabilityId = Guid.CreateVersion7(), ReviewerHumanId = Guid.CreateVersion7() }
        ])).Throws<ArgumentException>();
        await Assert.That(() => Source([reviewer.ToTerms() with { QualificationBasis = "" }]))
            .Throws<ArgumentException>();
    }

    internal static ModerationConstitutionTerms Terms() => new(
        ModerationScopeKind.Tenant, ModerationConstitutionEvidence.QualifiedFindingsAndResponse,
        ModerationConstitutionRemedies.RestrictedAuthenticationAndProofBoundRecovery,
        ModerationConstitutionIndependence.DistinctControllersAndImplicatedPartyExclusion,
        ModerationConstitutionExposure.ConcealedPurposeAuthorizedReveal,
        ModerationConstitutionOverload.AcknowledgeAndEscalate,
        1, 6, TimeSpan.FromDays(30), TimeSpan.FromHours(1),
        "https://example.invalid/moderation/support", "https://example.invalid/independent-redress",
        Guid.CreateVersion7(), Guid.CreateVersion7());

    private static ModerationConstitutionSource Source(IEnumerable<ModerationConstitutionReviewerTerms>? reviewers = null) =>
        ModerationConstitutionSource.Record(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), 1, Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), 1, new string('a', 64), Terms(), reviewers ??
            [new(Guid.CreateVersion7(), 1, 2, Guid.CreateVersion7(), Guid.CreateVersion7(),
                "Verified canonical controller through native identity review.",
                "Qualified for evidence assessment and protected remedy review.",
                "Independent review excludes implicated staff and controllers.")],
            Guid.CreateVersion7(), 4, Now, Now.AddMinutes(5));
}
