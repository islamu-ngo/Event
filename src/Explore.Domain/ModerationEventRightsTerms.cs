using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

public enum ModerationEventContinuity { ConsentingQualifiedStewardThenAccountableFallback = 1 }
public enum ModerationEventStewardConsent { SelfRecordedAndCurrentNativeDutyPermission = 1 }
public enum ModerationEventMissingSteward { PauseOnlyDependentRegistrationsAndEscalate = 1 }
public enum ModerationEventPhysicalJurisdiction { ExplicitPremisesEventSessionScopeAndCurrentHostPermission = 1 }
public enum ModerationEventSettlement { QualifiedActualRightsPartitionAndCompleteNativeCauses = 1 }
public enum ModerationEventMissingSettlement { NonurgentUnavailableUrgentNecessaryProtectionWithOwnedPendingSettlement = 1 }
public enum ModerationEventCompanionRights { PreserveInnocentCompanionsAndUnrelatedRights = 1 }
public enum ModerationEventHistoricalCommerce { AcceptedMerchantCurrencyMinorUnitsAndNativeIdempotency = 1 }
public enum ModerationEventRightsCorrection { RemoveOnlyAffectedContributionWithoutRightsResurrection = 1 }

/// <summary>
/// Substantive adopted event-rights obligations, not an event permission, willing steward,
/// physical jurisdiction or operational settlement attestation. Native consequences must
/// independently qualify the actual target, participant partition and current owner mappings.
/// </summary>
public sealed record ModerationEventRightsTerms(
    ModerationScopeKind ScopeCeiling,
    ModerationEventContinuity Continuity,
    ModerationEventStewardConsent Consent,
    ModerationEventMissingSteward MissingSteward,
    ModerationEventPhysicalJurisdiction PhysicalJurisdiction,
    ModerationEventSettlement Settlement,
    ModerationEventMissingSettlement MissingSettlement,
    ModerationEventCompanionRights Companions,
    ModerationEventHistoricalCommerce HistoricalCommerce,
    ModerationEventRightsCorrection Correction,
    string EventRightsGrounds,
    string ContinuityGrounds,
    string PremisesJurisdictionGrounds,
    string SettlementMappingGrounds)
{
    public void Validate()
    {
        if (!Enum.IsDefined(ScopeCeiling) || ScopeCeiling < ModerationScopeKind.Instance
            || Continuity != ModerationEventContinuity.ConsentingQualifiedStewardThenAccountableFallback
            || Consent != ModerationEventStewardConsent.SelfRecordedAndCurrentNativeDutyPermission
            || MissingSteward != ModerationEventMissingSteward.PauseOnlyDependentRegistrationsAndEscalate
            || PhysicalJurisdiction != ModerationEventPhysicalJurisdiction.ExplicitPremisesEventSessionScopeAndCurrentHostPermission
            || Settlement != ModerationEventSettlement.QualifiedActualRightsPartitionAndCompleteNativeCauses
            || MissingSettlement != ModerationEventMissingSettlement.NonurgentUnavailableUrgentNecessaryProtectionWithOwnedPendingSettlement
            || Companions != ModerationEventCompanionRights.PreserveInnocentCompanionsAndUnrelatedRights
            || HistoricalCommerce != ModerationEventHistoricalCommerce.AcceptedMerchantCurrencyMinorUnitsAndNativeIdempotency
            || Correction != ModerationEventRightsCorrection.RemoveOnlyAffectedContributionWithoutRightsResurrection
            || !Grounds(EventRightsGrounds) || !Grounds(ContinuityGrounds)
            || !Grounds(PremisesJurisdictionGrounds) || !Grounds(SettlementMappingGrounds))
            throw new ArgumentException("Event rights require explicit bounded continuity, physical scope, native settlement and independent remedy obligations.");
    }

    public ModerationEventContinuityDeadline FallbackDeadline(DateTimeOffset activatedAt, DateTimeOffset? eventStartsAt)
    {
        Validate();
        return ModerationEventContinuityDeadline.Create(activatedAt, eventStartsAt);
    }

    internal static bool Grounds(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 2048;
    public override string ToString() => nameof(ModerationEventRightsTerms);
}

/// <summary>
/// Accountable governance of the required native mappings. These humans/controllers and
/// substantive mandates do not confer actual host, steward, finance or merchant permission.
/// Such permission and willing acceptance remain with the existing event/settlement owners.
/// </summary>
public sealed record ModerationEventRightsOwnership(
    Guid ReviewerHumanId, Guid ReviewerControllerHumanId,
    Guid ContinuityMappingOwnerHumanId, Guid ContinuityMappingControllerHumanId,
    Guid SettlementMappingOwnerHumanId, Guid SettlementMappingControllerHumanId,
    Guid GovernanceControllerHumanId,
    string ControllerAndIndependenceGrounds,
    string RightsReviewMandateGrounds,
    string ContinuityMappingMandateGrounds,
    string SettlementMappingMandateGrounds)
{
    public void Validate(Guid recorder)
    {
        if (new[] { recorder, ReviewerHumanId, ReviewerControllerHumanId,
                ContinuityMappingOwnerHumanId, ContinuityMappingControllerHumanId,
                SettlementMappingOwnerHumanId, SettlementMappingControllerHumanId, GovernanceControllerHumanId }
            .Any(id => id == Guid.Empty)
            || ReviewerHumanId == recorder || ReviewerHumanId == ContinuityMappingOwnerHumanId
            || ReviewerHumanId == SettlementMappingOwnerHumanId
            || ReviewerControllerHumanId == GovernanceControllerHumanId
            || ReviewerControllerHumanId == ContinuityMappingControllerHumanId
            || ReviewerControllerHumanId == SettlementMappingControllerHumanId
            || !ModerationEventRightsTerms.Grounds(ControllerAndIndependenceGrounds)
            || !ModerationEventRightsTerms.Grounds(RightsReviewMandateGrounds)
            || !ModerationEventRightsTerms.Grounds(ContinuityMappingMandateGrounds)
            || !ModerationEventRightsTerms.Grounds(SettlementMappingMandateGrounds))
            throw new ArgumentException("Event rights require independent native reviewer/controller and explicit governance mandates for continuity and settlement mappings.");
    }

    public override string ToString() => nameof(ModerationEventRightsOwnership);
}
