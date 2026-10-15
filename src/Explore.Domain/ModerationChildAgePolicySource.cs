using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

/// <summary>
/// Immutable independently recorded child-age obligations for one exact native policy publication,
/// profile and mode. No subject DOB, contact, consent proof, guardian evidence or age assertion
/// belongs here. Neither this source nor its adoption activates collection or audience restrictions.
/// </summary>
public sealed class ModerationChildAgePolicySource
{
    private ModerationChildAgePolicySource() { }
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
    public ModerationChildAgeCollection Collection { get; private set; }
    public ModerationChildAgeConsent Consent { get; private set; }
    public ModerationChildAgeAssurance Assurance { get; private set; }
    public ModerationChildAgeProviderAssurance ProviderAssurance { get; private set; }
    public ModerationChildAgeEvidence Evidence { get; private set; }
    public ModerationChildAgeClassification Classification { get; private set; }
    public int DebateMinimumAge { get; private set; }
    public ModerationChildAgeAudience Audience { get; private set; }
    public ModerationChildAgeParticipant Participant { get; private set; }
    public ModerationChildAgeCorrection Correction { get; private set; }
    public ModerationChildAgeProtectedAccess ProtectedAccess { get; private set; }
    public ModerationChildAgeProhibitedInference ProhibitedInference { get; private set; }
    public ModerationChildAgeCalendar Calendar { get; private set; }
    public ModerationChildAgeTimeZone TimeZone { get; private set; }
    public ModerationChildAgeLeapDay LeapDay { get; private set; }
    public ModerationChildAgeAdulthood Adulthood { get; private set; }
    public int AdulthoodAge { get; private set; }
    public string ChildRiskGrounds { get; private set; } = string.Empty;
    public string PrivacyLegalGrounds { get; private set; } = string.Empty;
    public string LawfulCollectionPurpose { get; private set; } = string.Empty;
    public string ConsentGuardianApplicabilityGrounds { get; private set; } = string.Empty;
    public string ManualAssuranceQualificationGrounds { get; private set; } = string.Empty;
    public string? ProviderAssuranceReviewGrounds { get; private set; }
    public string ClassificationRationale { get; private set; } = string.Empty;
    public string CalendarJurisdictionGrounds { get; private set; } = string.Empty;
    public Guid ReviewerHumanId { get; private set; }
    public Guid ReviewerControllerHumanId { get; private set; }
    public Guid CollectionOwnerHumanId { get; private set; }
    public Guid CollectionControllerHumanId { get; private set; }
    public Guid GovernanceControllerHumanId { get; private set; }
    public string IdentityAndIndependenceBasis { get; private set; } = string.Empty;
    public string PrivacyLegalMandateBasis { get; private set; } = string.Empty;
    public string ChildSafeguardingMandateBasis { get; private set; } = string.Empty;
    public string CollectionMandateBasis { get; private set; } = string.Empty;
    public Guid RecordedByHumanId { get; private set; }
    public long WriterGeneration { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }

    public ModerationChildAgePolicyTerms Terms => new(ScopeCeiling, Collection, Consent, Assurance,
        ProviderAssurance, Evidence, Classification, DebateMinimumAge, Audience, Participant, Correction,
        ProtectedAccess, ProhibitedInference, Calendar, TimeZone, LeapDay, Adulthood, AdulthoodAge,
        ChildRiskGrounds, PrivacyLegalGrounds, LawfulCollectionPurpose, ConsentGuardianApplicabilityGrounds,
        ManualAssuranceQualificationGrounds, ProviderAssuranceReviewGrounds, ClassificationRationale, CalendarJurisdictionGrounds);
    public ModerationChildAgePolicyOwnership Ownership => new(ReviewerHumanId, ReviewerControllerHumanId,
        CollectionOwnerHumanId, CollectionControllerHumanId, GovernanceControllerHumanId,
        IdentityAndIndependenceBasis, PrivacyLegalMandateBasis, ChildSafeguardingMandateBasis, CollectionMandateBasis);

    public static ModerationChildAgePolicySource Record(
        Guid id, Guid hostId, Guid trustId, Guid tenantId, Guid policyId, long policyRevision,
        Guid publicationId, Guid definitionId, Guid profileId, int modeCode, string proposedStateDigest,
        ModerationChildAgePolicyTerms terms, ModerationChildAgePolicyOwnership ownership,
        Guid humanId, long writerGeneration, DateTimeOffset now, DateTimeOffset validUntil)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(ownership);
        terms.Validate();
        ownership.Validate(humanId);
        var source = new ModerationChildAgePolicySource
        {
            Id = id, HostBindingId = hostId, TrustDomainId = trustId, TenantId = tenantId,
            PolicyId = policyId, PolicyRevision = policyRevision, PublicationId = publicationId,
            DefinitionId = definitionId, ProfileId = profileId, ModeCode = modeCode, ProposedStateDigest = proposedStateDigest,
            ScopeCeiling = terms.ScopeCeiling, Collection = terms.Collection, Consent = terms.Consent,
            Assurance = terms.Assurance, ProviderAssurance = terms.ProviderAssurance, Evidence = terms.Evidence,
            Classification = terms.Classification, DebateMinimumAge = terms.DebateMinimumAge,
            Audience = terms.Audience, Participant = terms.Participant, Correction = terms.Correction,
            ProtectedAccess = terms.ProtectedAccess, ProhibitedInference = terms.ProhibitedInference,
            Calendar = terms.Calendar, TimeZone = terms.TimeZone, LeapDay = terms.LeapDay,
            Adulthood = terms.Adulthood, AdulthoodAge = terms.AdulthoodAge,
            ChildRiskGrounds = terms.ChildRiskGrounds, PrivacyLegalGrounds = terms.PrivacyLegalGrounds,
            LawfulCollectionPurpose = terms.LawfulCollectionPurpose,
            ConsentGuardianApplicabilityGrounds = terms.ConsentGuardianApplicabilityGrounds,
            ManualAssuranceQualificationGrounds = terms.ManualAssuranceQualificationGrounds,
            ProviderAssuranceReviewGrounds = terms.ProviderAssuranceReviewGrounds,
            ClassificationRationale = terms.ClassificationRationale, CalendarJurisdictionGrounds = terms.CalendarJurisdictionGrounds,
            ReviewerHumanId = ownership.ReviewerHumanId, ReviewerControllerHumanId = ownership.ReviewerControllerHumanId,
            CollectionOwnerHumanId = ownership.CollectionOwnerHumanId, CollectionControllerHumanId = ownership.CollectionControllerHumanId,
            GovernanceControllerHumanId = ownership.GovernanceControllerHumanId,
            IdentityAndIndependenceBasis = ownership.IdentityAndIndependenceBasis,
            PrivacyLegalMandateBasis = ownership.PrivacyLegalMandateBasis,
            ChildSafeguardingMandateBasis = ownership.ChildSafeguardingMandateBasis,
            CollectionMandateBasis = ownership.CollectionMandateBasis,
            RecordedByHumanId = humanId, WriterGeneration = writerGeneration, Revision = 1, RecordedAt = now, ValidUntil = validUntil
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
            || WriterGeneration is < 1 or long.MaxValue
            || ProposedStateDigest is not { Length: 64 } || !ProposedStateDigest.All(char.IsAsciiHexDigit))
            throw new ArgumentException("Child-age source requires exact native coordinates, immutable generation and finite validity.");
    }

    public override string ToString() => nameof(ModerationChildAgePolicySource);
}
