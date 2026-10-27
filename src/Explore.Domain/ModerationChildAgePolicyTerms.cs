using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

public enum ModerationChildAgeCollection { LawfulMinimumBeforeSensitiveBodyHandling = 1 }
public enum ModerationChildAgeConsent { ActualSubjectPurposePolicyBoundPrerequisites = 1 }
public enum ModerationChildAgeAssurance { QualifiedLocalManualThresholdNotDeclaredDob = 1 }
public enum ModerationChildAgeProviderAssurance { NotAccepted = 1, OptionalReviewedThresholdOnly = 2 }
public enum ModerationChildAgeEvidence { UnknownContradictoryExpiredRemainRestrictedNotGuilty = 1 }
public enum ModerationChildAgeClassification { ReviewedContentNotEventFormat = 1 }
public enum ModerationChildAgeAudience { WholeEventUnlessCompleteSeparatelyQualifiedSegregation = 1 }
public enum ModerationChildAgeParticipant { ActualViewerAndParticipantNeverPurchaser = 1 }
public enum ModerationChildAgeCorrection { InvalidateDerivedAgeWithoutStrikeOrRecognitionRestoration = 1 }
public enum ModerationChildAgeProtectedAccess { CatalogueRemedyOrdinaryErasureIndependent = 1 }
public enum ModerationChildAgeProhibitedInference { NoChildReligionBehaviourOrUniversalIdentityFace = 1 }
public enum ModerationChildAgeCalendar { Gregorian = 1 }
public enum ModerationChildAgeTimeZone { Utc = 1 }
public enum ModerationChildAgeLeapDay { MarchFirstInNonLeapYear = 1 }
public enum ModerationChildAgeAdulthood { ReconsiderGuardianGrantsAndPurposes = 1 }

/// <summary>
/// Normative AG04 obligations, not installed collection, assurance, classification or segregation.
/// Collection requires a lawful-minimum gate and a subject or registration-session, purpose and
/// policy-revision-bound receipt before normal DOB/profile/participant PII acceptance, logging,
/// persistence or provider ingestion. This covers initial native/external enrollment, existing
/// profile completion and actual guest collection. Unsupported controls keep the purpose closed.
/// Consent/guardian prerequisites are assessed for that subject where applicable; an adult payer
/// cannot consent for participants. Declared DOB alone is not accepted threshold assurance.
/// Whole-event restriction remains the default until every session, resource, preview, entitlement
/// and door path has independently passed segregation conformance. Correction/erasure invalidates
/// derived age state without strikes or reconstruction of DOB/history through recognition.
/// The Debate floor of 18 is an ISLAMU adoption, never universal legal or religious certification.
/// Review grounds contain policy reasoning only, never raw DOB, contact or subject evidence.
/// </summary>
public sealed record ModerationChildAgePolicyTerms(
    ModerationScopeKind ScopeCeiling,
    ModerationChildAgeCollection Collection,
    ModerationChildAgeConsent Consent,
    ModerationChildAgeAssurance Assurance,
    ModerationChildAgeProviderAssurance ProviderAssurance,
    ModerationChildAgeEvidence Evidence,
    ModerationChildAgeClassification Classification,
    int DebateMinimumAge,
    ModerationChildAgeAudience Audience,
    ModerationChildAgeParticipant Participant,
    ModerationChildAgeCorrection Correction,
    ModerationChildAgeProtectedAccess ProtectedAccess,
    ModerationChildAgeProhibitedInference ProhibitedInference,
    ModerationChildAgeCalendar Calendar,
    ModerationChildAgeTimeZone TimeZone,
    ModerationChildAgeLeapDay LeapDay,
    ModerationChildAgeAdulthood Adulthood,
    int AdulthoodAge,
    string ChildRiskGrounds,
    string PrivacyLegalGrounds,
    string LawfulCollectionPurpose,
    string ConsentGuardianApplicabilityGrounds,
    string ManualAssuranceQualificationGrounds,
    string? ProviderAssuranceReviewGrounds,
    string ClassificationRationale,
    string CalendarJurisdictionGrounds)
{
    public void Validate()
    {
        if (!Enum.IsDefined(ScopeCeiling) || ScopeCeiling < ModerationScopeKind.Instance
            || Collection != ModerationChildAgeCollection.LawfulMinimumBeforeSensitiveBodyHandling
            || Consent != ModerationChildAgeConsent.ActualSubjectPurposePolicyBoundPrerequisites
            || Assurance != ModerationChildAgeAssurance.QualifiedLocalManualThresholdNotDeclaredDob
            || !Enum.IsDefined(ProviderAssurance)
            || ProviderAssurance == ModerationChildAgeProviderAssurance.NotAccepted && ProviderAssuranceReviewGrounds is not null
            || ProviderAssurance == ModerationChildAgeProviderAssurance.OptionalReviewedThresholdOnly && !Grounds(ProviderAssuranceReviewGrounds)
            || Evidence != ModerationChildAgeEvidence.UnknownContradictoryExpiredRemainRestrictedNotGuilty
            || Classification != ModerationChildAgeClassification.ReviewedContentNotEventFormat
            || DebateMinimumAge != 18
            || Audience != ModerationChildAgeAudience.WholeEventUnlessCompleteSeparatelyQualifiedSegregation
            || Participant != ModerationChildAgeParticipant.ActualViewerAndParticipantNeverPurchaser
            || Correction != ModerationChildAgeCorrection.InvalidateDerivedAgeWithoutStrikeOrRecognitionRestoration
            || ProtectedAccess != ModerationChildAgeProtectedAccess.CatalogueRemedyOrdinaryErasureIndependent
            || ProhibitedInference != ModerationChildAgeProhibitedInference.NoChildReligionBehaviourOrUniversalIdentityFace
            || Calendar != ModerationChildAgeCalendar.Gregorian || TimeZone != ModerationChildAgeTimeZone.Utc
            || LeapDay != ModerationChildAgeLeapDay.MarchFirstInNonLeapYear
            || Adulthood != ModerationChildAgeAdulthood.ReconsiderGuardianGrantsAndPurposes || AdulthoodAge is < 1 or > 130
            || !Grounds(ChildRiskGrounds) || !Grounds(PrivacyLegalGrounds) || !Grounds(LawfulCollectionPurpose)
            || !Grounds(ConsentGuardianApplicabilityGrounds) || !Grounds(ManualAssuranceQualificationGrounds)
            || !Grounds(ClassificationRationale) || !Grounds(CalendarJurisdictionGrounds))
            throw new ArgumentException("Child-age policy requires reviewed grounds, minimum subject-bound collection, independent remedies, qualified assurance and explicit calendar/classification safeguards.");
    }

    internal static bool Grounds(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 2048;
    public override string ToString() => nameof(ModerationChildAgePolicyTerms);
}

/// <summary>
/// A nominated reviewer must hold both substantive privacy/legal and child-safeguarding mandates.
/// Native administrator status is necessary for this governance write, not evidence of either
/// mandate. The collection owner, reviewer and governance recorder have independently reviewed
/// canonical controllers. These minimized statements are not professional certification.
/// </summary>
public sealed record ModerationChildAgePolicyOwnership(
    Guid ReviewerHumanId, Guid ReviewerControllerHumanId,
    Guid CollectionOwnerHumanId, Guid CollectionControllerHumanId, Guid GovernanceControllerHumanId,
    string IdentityAndIndependenceBasis, string PrivacyLegalMandateBasis,
    string ChildSafeguardingMandateBasis, string CollectionMandateBasis)
{
    public void Validate(Guid recorder)
    {
        var controllers = new[] { ReviewerControllerHumanId, CollectionControllerHumanId, GovernanceControllerHumanId };
        if (recorder == Guid.Empty || ReviewerHumanId == Guid.Empty || CollectionOwnerHumanId == Guid.Empty
            || controllers.Any(id => id == Guid.Empty) || controllers.Distinct().Count() != 3
            || ReviewerHumanId == CollectionOwnerHumanId || ReviewerHumanId == recorder || CollectionOwnerHumanId == recorder
            || ReviewerHumanId == CollectionControllerHumanId || ReviewerHumanId == GovernanceControllerHumanId
            || CollectionOwnerHumanId == ReviewerControllerHumanId || CollectionOwnerHumanId == GovernanceControllerHumanId
            || recorder == ReviewerControllerHumanId || recorder == CollectionControllerHumanId
            || !ModerationChildAgePolicyTerms.Grounds(IdentityAndIndependenceBasis)
            || !ModerationChildAgePolicyTerms.Grounds(PrivacyLegalMandateBasis)
            || !ModerationChildAgePolicyTerms.Grounds(ChildSafeguardingMandateBasis)
            || !ModerationChildAgePolicyTerms.Grounds(CollectionMandateBasis))
            throw new ArgumentException("Child-age governance requires independent controllers, dual reviewer mandates and accountable collection ownership.");
    }

    public override string ToString() => nameof(ModerationChildAgePolicyOwnership);
}
