using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationEventRightsTermsTests
{
    [Test]
    public async Task Continuity_deadline_uses_the_original_activation_and_does_not_certify_unknown_schedules()
    {
        var terms = Terms();
        terms.Validate();
        var activated = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        await Assert.That(terms.FallbackDeadline(activated, activated.AddHours(12)).DueAt)
            .IsEqualTo(activated.AddHours(10));
        await Assert.That(terms.FallbackDeadline(activated, activated.AddHours(48)).DueAt)
            .IsEqualTo(activated.AddHours(24));
        await Assert.That(terms.FallbackDeadline(activated, null).IsDue(activated)).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(8)]
    [Arguments(9)]
    [Arguments(10)]
    [Arguments(11)]
    [Arguments(12)]
    [Arguments(13)]
    [Arguments(14)]
    public async Task Unsupported_or_incomplete_rights_obligations_cannot_be_adopted(int change)
    {
        var terms = Terms();
        terms = change switch
        {
            0 => terms with { ScopeCeiling = ModerationScopeKind.TrustDomain },
            1 => terms with { Continuity = (ModerationEventContinuity)2 },
            2 => terms with { Consent = (ModerationEventStewardConsent)2 },
            3 => terms with { MissingSteward = (ModerationEventMissingSteward)2 },
            4 => terms with { PhysicalJurisdiction = (ModerationEventPhysicalJurisdiction)2 },
            5 => terms with { Settlement = (ModerationEventSettlement)2 },
            6 => terms with { MissingSettlement = (ModerationEventMissingSettlement)2 },
            7 => terms with { Companions = (ModerationEventCompanionRights)2 },
            8 => terms with { HistoricalCommerce = (ModerationEventHistoricalCommerce)2 },
            9 => terms with { Correction = (ModerationEventRightsCorrection)2 },
            10 => terms with { EventRightsGrounds = "" },
            11 => terms with { ContinuityGrounds = new string('x', 2049) },
            12 => terms with { PremisesJurisdictionGrounds = "" },
            13 => terms with { SettlementMappingGrounds = "" },
            _ => terms with { ScopeCeiling = (ModerationScopeKind)int.MaxValue }
        };
        await Assert.That(terms.Validate).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("recorder")]
    [Arguments("continuity")]
    [Arguments("settlement")]
    [Arguments("recorder-controller")]
    [Arguments("continuity-controller")]
    [Arguments("settlement-controller")]
    [Arguments("missing-mandate")]
    public async Task Independent_rights_review_cannot_be_supplied_by_the_recorder_or_covered_mapping_owners(string change)
    {
        var recorder = Guid.CreateVersion7();
        var reviewer = Guid.CreateVersion7();
        var continuity = Guid.CreateVersion7();
        var settlement = Guid.CreateVersion7();
        var ownership = new ModerationEventRightsOwnership(reviewer, reviewer,
            continuity, continuity, settlement, settlement, recorder,
            "Independently reviewed canonical controllers.",
            "Accountable rights and premises review mandate.",
            "Accountable native continuity mapping mandate.",
            "Accountable native settlement mapping mandate.");
        ownership = change switch
        {
            "recorder" => ownership with { ReviewerHumanId = recorder },
            "continuity" => ownership with { ReviewerHumanId = continuity },
            "settlement" => ownership with { ReviewerHumanId = settlement },
            "recorder-controller" => ownership with { ReviewerControllerHumanId = recorder },
            "continuity-controller" => ownership with { ReviewerControllerHumanId = continuity },
            "settlement-controller" => ownership with { ReviewerControllerHumanId = settlement },
            _ => ownership with { RightsReviewMandateGrounds = "" }
        };
        await Assert.That(() => ownership.Validate(recorder)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Mapping_governance_does_not_require_two_different_native_host_and_finance_operators()
    {
        var recorder = Guid.CreateVersion7();
        var reviewer = Guid.CreateVersion7();
        var mappingOwner = Guid.CreateVersion7();
        var ownership = new ModerationEventRightsOwnership(reviewer, reviewer, mappingOwner, mappingOwner,
            mappingOwner, mappingOwner, recorder, "Independent review controller.",
            "Rights and premises governance mandate.", "Continuity mapping mandate.", "Settlement mapping mandate.");
        ownership.Validate(recorder);
        await Assert.That(ownership.ContinuityMappingOwnerHumanId).IsEqualTo(ownership.SettlementMappingOwnerHumanId);
    }

    internal static ModerationEventRightsTerms Terms() => new(ModerationScopeKind.Tenant,
        ModerationEventContinuity.ConsentingQualifiedStewardThenAccountableFallback,
        ModerationEventStewardConsent.SelfRecordedAndCurrentNativeDutyPermission,
        ModerationEventMissingSteward.PauseOnlyDependentRegistrationsAndEscalate,
        ModerationEventPhysicalJurisdiction.ExplicitPremisesEventSessionScopeAndCurrentHostPermission,
        ModerationEventSettlement.QualifiedActualRightsPartitionAndCompleteNativeCauses,
        ModerationEventMissingSettlement.NonurgentUnavailableUrgentNecessaryProtectionWithOwnedPendingSettlement,
        ModerationEventCompanionRights.PreserveInnocentCompanionsAndUnrelatedRights,
        ModerationEventHistoricalCommerce.AcceptedMerchantCurrencyMinorUnitsAndNativeIdempotency,
        ModerationEventRightsCorrection.RemoveOnlyAffectedContributionWithoutRightsResurrection,
        "Reviewed event rights and existing native owner boundaries.",
        "Essential denied duties, explicit willing steward, accountable deadline and scoped dependent pause.",
        "Separately reviewed event/session/premises jurisdiction and host authority.",
        "Explicit participant partitions and existing refund, capacity, promotion, waitlist and provider owners.");
}
