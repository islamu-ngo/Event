namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationEventRightsSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Adoption_requires_the_nominated_independent_reviewer_and_does_not_renew_source()
    {
        var source = Source();
        foreach (var human in new[] { source.RecordedByHumanId, source.ContinuityMappingOwnerHumanId,
            source.SettlementMappingOwnerHumanId, Guid.CreateVersion7() })
            await Assert.That(() => ModerationEventRightsAdoption.Adopt(Guid.CreateVersion7(), source,
                human, 5, Now, Now.AddMinutes(4))).Throws<ArgumentException>();
        await Assert.That(() => ModerationEventRightsAdoption.Adopt(Guid.CreateVersion7(), source,
            source.ReviewerHumanId, 4, Now, Now.AddMinutes(4))).Throws<ArgumentException>();
        await Assert.That(() => ModerationEventRightsAdoption.Adopt(Guid.CreateVersion7(), source,
            source.ReviewerHumanId, 5, Now, source.ValidUntil.AddTicks(1))).Throws<ArgumentException>();
        var adoption = ModerationEventRightsAdoption.Adopt(Guid.CreateVersion7(), source,
            source.ReviewerHumanId, 5, Now, Now.AddMinutes(4));
        await Assert.That(adoption.MatchesCurrent(source, Now)).IsTrue();
        await Assert.That(adoption.MatchesCurrent(Source(), Now)).IsFalse();
        await Assert.That(adoption.MatchesCurrent(source, adoption.ValidUntil)).IsFalse();
        await Assert.That(adoption.MatchesCurrent(source, Now.AddTicks(-1))).IsFalse();
        await Assert.That(source.ValidUntil).IsEqualTo(Now.AddMinutes(5));
    }

    [Test]
    [Arguments(-1)]
    [Arguments(1)]
    public async Task Current_adoption_requires_a_UTC_observation_even_for_the_same_instant(int offsetHours)
    {
        var source = Source();
        var adoption = ModerationEventRightsAdoption.Adopt(Guid.CreateVersion7(), source,
            source.ReviewerHumanId, 5, Now, Now.AddMinutes(4));
        await Assert.That(adoption.MatchesCurrent(source, Now)).IsTrue();
        await Assert.That(adoption.MatchesCurrent(source, Now.ToOffset(TimeSpan.FromHours(offsetHours)))).IsFalse();
    }

    [Test]
    public async Task Withdrawal_after_expiry_preserves_provenance_and_is_terminal()
    {
        var source = Source();
        var adoption = ModerationEventRightsAdoption.Adopt(Guid.CreateVersion7(), source,
            source.ReviewerHumanId, 5, Now, Now.AddMinutes(4));
        var withdrawer = Guid.CreateVersion7();
        adoption.Withdraw(1, withdrawer, Now.AddDays(1));
        adoption.ValidatePersistedState();
        await Assert.That(adoption.Revision).IsEqualTo(2);
        await Assert.That(adoption.AdoptedByHumanId).IsEqualTo(source.ReviewerHumanId);
        await Assert.That(adoption.SourceWriterGeneration).IsEqualTo(4);
        await Assert.That(adoption.WriterGeneration).IsEqualTo(5);
        await Assert.That(adoption.WithdrawnByHumanId).IsEqualTo(withdrawer);
        await Assert.That(adoption.MatchesCurrent(source, Now)).IsFalse();
        await Assert.That(() => adoption.Withdraw(2, withdrawer, Now.AddDays(2))).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("identity")]
    [Arguments("policy-revision")]
    [Arguments("mode")]
    [Arguments("digest")]
    [Arguments("generation")]
    [Arguments("expiry")]
    [Arguments("non-utc")]
    public async Task Source_rejects_unbound_or_unbounded_coordinates(string change)
    {
        var ownership = Ownership();
        await Assert.That(() => ModerationEventRightsSource.Record(
            change == "identity" ? Guid.Empty : Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), change == "policy-revision" ? 0 : 1,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), change == "mode" ? 3 : 1,
            change == "digest" ? "unbound" : new string('a', 64), ModerationEventRightsTermsTests.Terms(), ownership,
            ownership.GovernanceControllerHumanId, change == "generation" ? 0 : 4,
            change == "non-utc" ? Now.ToOffset(TimeSpan.FromHours(1)) : Now,
            change == "expiry" ? DateTimeOffset.MaxValue : Now.AddMinutes(5))).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("independence")]
    [Arguments("review")]
    [Arguments("continuity")]
    [Arguments("settlement")]
    public async Task Native_admin_identity_never_substitutes_for_substantive_governance_mandates(string change)
    {
        var ownership = Ownership();
        ownership = change switch
        {
            "independence" => ownership with { ControllerAndIndependenceGrounds = "" },
            "review" => ownership with { RightsReviewMandateGrounds = "" },
            "continuity" => ownership with { ContinuityMappingMandateGrounds = "" },
            _ => ownership with { SettlementMappingMandateGrounds = "" }
        };
        await Assert.That(() => Source(ownership)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Source_retains_exact_parent_terms_and_separate_mapping_governance()
    {
        var terms = ModerationEventRightsTermsTests.Terms();
        var ownership = Ownership();
        var source = Source(ownership);
        await Assert.That(source.Terms).IsEqualTo(terms);
        await Assert.That(source.Ownership).IsEqualTo(ownership);
        await Assert.That(source.Terms.FallbackDeadline(Now, Now.AddHours(12)).DueAt).IsEqualTo(Now.AddHours(10));
        await Assert.That(source.ToString()).IsEqualTo(nameof(ModerationEventRightsSource));
    }

    private static ModerationEventRightsOwnership Ownership()
    {
        var reviewer = Guid.CreateVersion7();
        var continuity = Guid.CreateVersion7();
        var settlement = Guid.CreateVersion7();
        return new(reviewer, reviewer, continuity, continuity, settlement, settlement, Guid.CreateVersion7(),
            "Reviewed independent canonical controllers.",
            "Independent rights and premises review mandate.",
            "Accountable governance of native continuity mapping.",
            "Accountable governance of native settlement mapping.");
    }

    private static ModerationEventRightsSource Source(ModerationEventRightsOwnership? ownership = null)
    {
        ownership ??= Ownership();
        return ModerationEventRightsSource.Record(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), 1, Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), 1, new string('a', 64), ModerationEventRightsTermsTests.Terms(), ownership,
            ownership.GovernanceControllerHumanId, 4, Now, Now.AddMinutes(5));
    }
}
