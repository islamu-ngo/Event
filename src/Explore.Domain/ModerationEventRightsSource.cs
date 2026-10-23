using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

/// <summary>
/// Immutable normative event-rights obligations for one native policy publication, profile and mode.
/// Accountable mapping governance does not attest actual event permissions, steward consent,
/// premises jurisdiction, participant rights or settlement availability.
/// </summary>
public sealed class ModerationEventRightsSource
{
    private ModerationEventRightsSource() { }
    public Guid Id { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid PolicyId { get; private set; }
    public long PolicyRevision { get; private set; }
    public Guid PublicationId { get; private set; }
    public Guid DefinitionId { get; private set; }
    public Guid ProfileId { get; private set; }
    public int ModeCode { get; private set; }
    public string ProposedStateDigest { get; private set; } = string.Empty;
    public ModerationScopeKind ScopeCeiling { get; private set; }
    public ModerationEventContinuity Continuity { get; private set; }
    public ModerationEventStewardConsent Consent { get; private set; }
    public ModerationEventMissingSteward MissingSteward { get; private set; }
    public ModerationEventPhysicalJurisdiction PhysicalJurisdiction { get; private set; }
    public ModerationEventSettlement Settlement { get; private set; }
    public ModerationEventMissingSettlement MissingSettlement { get; private set; }
    public ModerationEventCompanionRights Companions { get; private set; }
    public ModerationEventHistoricalCommerce HistoricalCommerce { get; private set; }
    public ModerationEventRightsCorrection Correction { get; private set; }
    public string EventRightsGrounds { get; private set; } = string.Empty;
    public string ContinuityGrounds { get; private set; } = string.Empty;
    public string PremisesJurisdictionGrounds { get; private set; } = string.Empty;
    public string SettlementMappingGrounds { get; private set; } = string.Empty;
    public Guid ReviewerHumanId { get; private set; }
    public Guid ReviewerControllerHumanId { get; private set; }
    public Guid ContinuityMappingOwnerHumanId { get; private set; }
    public Guid ContinuityMappingControllerHumanId { get; private set; }
    public Guid SettlementMappingOwnerHumanId { get; private set; }
    public Guid SettlementMappingControllerHumanId { get; private set; }
    public Guid GovernanceControllerHumanId { get; private set; }
    public string ControllerAndIndependenceGrounds { get; private set; } = string.Empty;
    public string RightsReviewMandateGrounds { get; private set; } = string.Empty;
    public string ContinuityMappingMandateGrounds { get; private set; } = string.Empty;
    public string SettlementMappingMandateGrounds { get; private set; } = string.Empty;
    public Guid RecordedByHumanId { get; private set; }
    public long WriterGeneration { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }

    public ModerationEventRightsTerms Terms => new(ScopeCeiling, Continuity, Consent, MissingSteward,
        PhysicalJurisdiction, Settlement, MissingSettlement, Companions, HistoricalCommerce, Correction,
        EventRightsGrounds, ContinuityGrounds, PremisesJurisdictionGrounds, SettlementMappingGrounds);
    public ModerationEventRightsOwnership Ownership => new(ReviewerHumanId, ReviewerControllerHumanId,
        ContinuityMappingOwnerHumanId, ContinuityMappingControllerHumanId, SettlementMappingOwnerHumanId,
        SettlementMappingControllerHumanId, GovernanceControllerHumanId, ControllerAndIndependenceGrounds,
        RightsReviewMandateGrounds, ContinuityMappingMandateGrounds, SettlementMappingMandateGrounds);

    public static ModerationEventRightsSource Record(
        Guid id, Guid hostId, Guid trustId, Guid tenantId, Guid policyId, long policyRevision,
        Guid publicationId, Guid definitionId, Guid profileId, int modeCode, string proposedStateDigest,
        ModerationEventRightsTerms terms, ModerationEventRightsOwnership ownership,
        Guid humanId, long writerGeneration, DateTimeOffset now, DateTimeOffset validUntil)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(ownership);
        terms.Validate();
        ownership.Validate(humanId);
        var source = new ModerationEventRightsSource
        {
            Id = id,
            HostBindingId = hostId,
            TrustDomainId = trustId,
            TenantId = tenantId,
            PolicyId = policyId,
            PolicyRevision = policyRevision,
            PublicationId = publicationId,
            DefinitionId = definitionId,
            ProfileId = profileId,
            ModeCode = modeCode,
            ProposedStateDigest = proposedStateDigest,
            ScopeCeiling = terms.ScopeCeiling,
            Continuity = terms.Continuity,
            Consent = terms.Consent,
            MissingSteward = terms.MissingSteward,
            PhysicalJurisdiction = terms.PhysicalJurisdiction,
            Settlement = terms.Settlement,
            MissingSettlement = terms.MissingSettlement,
            Companions = terms.Companions,
            HistoricalCommerce = terms.HistoricalCommerce,
            Correction = terms.Correction,
            EventRightsGrounds = terms.EventRightsGrounds,
            ContinuityGrounds = terms.ContinuityGrounds,
            PremisesJurisdictionGrounds = terms.PremisesJurisdictionGrounds,
            SettlementMappingGrounds = terms.SettlementMappingGrounds,
            ReviewerHumanId = ownership.ReviewerHumanId,
            ReviewerControllerHumanId = ownership.ReviewerControllerHumanId,
            ContinuityMappingOwnerHumanId = ownership.ContinuityMappingOwnerHumanId,
            ContinuityMappingControllerHumanId = ownership.ContinuityMappingControllerHumanId,
            SettlementMappingOwnerHumanId = ownership.SettlementMappingOwnerHumanId,
            SettlementMappingControllerHumanId = ownership.SettlementMappingControllerHumanId,
            GovernanceControllerHumanId = ownership.GovernanceControllerHumanId,
            ControllerAndIndependenceGrounds = ownership.ControllerAndIndependenceGrounds,
            RightsReviewMandateGrounds = ownership.RightsReviewMandateGrounds,
            ContinuityMappingMandateGrounds = ownership.ContinuityMappingMandateGrounds,
            SettlementMappingMandateGrounds = ownership.SettlementMappingMandateGrounds,
            RecordedByHumanId = humanId,
            WriterGeneration = writerGeneration,
            Revision = 1,
            RecordedAt = now,
            ValidUntil = validUntil
        };
        source.ValidatePersistedState();
        return source;
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, HostBindingId, TrustDomainId, TenantId, PolicyId, PublicationId, DefinitionId, ProfileId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        Terms.Validate();
        Ownership.Validate(RecordedByHumanId);
        ModerationQualificationPolicySource.RequireValidity(RecordedAt, ValidUntil);
        if (Revision != 1 || PolicyRevision is < 1 or long.MaxValue || ModeCode is < 1 or > 2
            || WriterGeneration is < 1 or long.MaxValue || !ModerationDeploymentSource.Digest(ProposedStateDigest))
            throw new ArgumentException("Event-rights source requires exact native coordinates, immutable generation and finite validity.");
    }

    public override string ToString() => nameof(ModerationEventRightsSource);
}
