namespace ISLAMU.Wire.Contracts.ConfigurationPortability;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>Exact named wire vocabulary: case aliases, numeric tokens and numeric strings are invalid.</summary>
public sealed class ConfigurationModerationEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String
            || !Enum.TryParse(reader.GetString(), ignoreCase: false, out T value)
            || !Enum.IsDefined(value)
            || !string.Equals(reader.GetString(), value.ToString(), StringComparison.Ordinal))
            throw new JsonException("Invalid moderation vocabulary.");
        return value;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        if (!Enum.IsDefined(value))
            throw new JsonException("Invalid moderation vocabulary.");
        writer.WriteStringValue(value.ToString());
    }
}

[JsonConverter(typeof(ConfigurationModerationEnumConverter<ConfigurationModerationMode>))]
public enum ConfigurationModerationMode { Embedded = 1, RemoteStrict = 2 }

[JsonConverter(typeof(ConfigurationModerationEnumConverter<ConfigurationModerationScopeKind>))]
public enum ConfigurationModerationScopeKind { TrustDomain = 1, Solution = 2, Instance = 3, Tenant = 4, Community = 5, Resource = 6, Event = 7 }

[JsonConverter(typeof(ConfigurationModerationEnumConverter<ConfigurationModerationSubjectKind>))]
public enum ConfigurationModerationSubjectKind { Human = 1, Service = 2, Organization = 3, Content = 4 }

[JsonConverter(typeof(ConfigurationModerationEnumConverter<ConfigurationModerationReferenceProfile>))]
public enum ConfigurationModerationReferenceProfile
{
    CFG01 = 1, CFG05 = 5, CFG06 = 6, CFG07 = 7, CFG08 = 8, CFG09 = 9, CFG10 = 10,
    CFG11 = 11, CFG12 = 12, CFG13 = 13, CFG14 = 14, CFG15 = 15, CFG16 = 16,
    CFG17 = 17, CFG18 = 18, CFG19 = 19, CFG20 = 20, CFG23 = 23, CFG24 = 24,
    CFG25 = 25, CFG26 = 26, CFG28 = 28, CFG29 = 29, CFG30 = 30, CFG31 = 31,
    CFG32 = 32, CFG33 = 33
}

[JsonConverter(typeof(ConfigurationModerationEnumConverter<ConfigurationModerationGrade>))]
public enum ConfigurationModerationGrade { Control = -1, G0 = 0, G1 = 1, G2 = 2, G3 = 3, G4 = 4 }

[JsonConverter(typeof(ConfigurationModerationEnumConverter<ConfigurationModerationBasis>))]
public enum ConfigurationModerationBasis
{
    ConfirmedWarning = 1, FinalConductFinding = 2, QualifiedUrgentProtection = 3,
    VerifiedUnsafeObject = 4, KnownProhibitedDefinition = 5, CurrentEligibility = 6, ClassificationCorrection = 7
}

[JsonConverter(typeof(ConfigurationModerationEnumConverter<ConfigurationModerationGround>))]
public enum ConfigurationModerationGround
{
    CompatibleConfirmedConduct = 1, PublicationMisuse = 2, PublicationAbuse = 3, UploadMisuse = 4,
    ConfidentialityOrAccessAbuse = 5, VerifiedUnsafeResource = 6, EventManagementMisuse = 7,
    PrivilegedTenantMisuse = 8, BroadTenantMisconduct = 9, BroadSolutionMisconduct = 10,
    IndependentlyEstablishedCrossSolutionHarm = 11, CredibleImminentPhysicalRisk = 12,
    IndependentlyConfirmedPhysicalMisconduct = 13, IndependentlyResolvedRecurrentPhysicalRisk = 14,
    SeriousContinuedPhysicalRisk = 15, ProvedRegistrationOrTicketAbuse = 16, CredibleCredentialCompromise = 17,
    ProvenAbusiveAutomation = 18, CredibleDeceptivePublicationRisk = 19,
    ConfirmedDeceptiveOrProhibitedPublication = 20, KnownProhibitedSecretSolicitation = 21,
    CurrentAdultAudienceEligibility = 22, ClassificationMismatchOrHonestError = 23,
    FirstFinalClassificationViolation = 24, SecondFinalClassificationViolation = 25,
    ThirdFinalClassificationViolation = 26, FourthOrJustifiedPersistentClassificationEvasion = 27
}

[JsonConverter(typeof(ConfigurationModerationEnumConverter<ConfigurationModerationRecognition>))]
public enum ConfigurationModerationRecognition { Never = 1, SeparatelyQualifiedFinalAction = 2, SeparatelyQualifiedFinalEnrollment = 3 }

[JsonConverter(typeof(ConfigurationModerationEnumConverter<ConfigurationModerationExecution>))]
public enum ConfigurationModerationExecution { OverlayDenial = 1, NativeOwner = 2 }

[JsonConverter(typeof(ConfigurationModerationEnumConverter<ConfigurationModerationEffectMode>))]
public enum ConfigurationModerationEffectMode { Notice = 1, Deny = 2, RequireReview = 3, Quarantine = 4, Guard = 5, Correct = 6, Hold = 7, Revoke = 8 }

[JsonConverter(typeof(ConfigurationModerationEnumConverter<ConfigurationModerationTarget>))]
public enum ConfigurationModerationTarget
{
    SelectedOperations = 1, SelectedResourceVersion = 2, SelectedTenant = 3, SelectedSolution = 4,
    EnrolledTrustDomain = 5, SelectedParticipantEventWindow = 6, SelectedCommunityParticipants = 7,
    ProvedBadEntitlements = 8, CompromisedCredentialOperations = 9,
    ImplicatedEventFormDestinationVersions = 10, ActualViewerAndParticipant = 11, ClassifiedContentVersions = 12
}

[JsonConverter(typeof(ConfigurationModerationEnumConverter<ConfigurationModerationEffectKind>))]
public enum ConfigurationModerationEffectKind
{
    ReasonedNotice = 1, NewAndMaterialPublication = 2, NewUploadAndResourcePublication = 3,
    SelectedResourceMetadataPayloadRedirect = 4, UnsafeResourceVersion = 5, SelectedEventManagement = 6,
    SelectedTenantAdministration = 7, DigitalJoinAndNewOnlineRegistration = 8, ProductEnrollment = 9,
    ParticipantAdmission = 10, SelectedPhysicalRegistration = 11, PrivateVenueAndDerivedDisclosure = 12,
    VenueSensitiveManagement = 13, VenueSensitiveResourceReads = 14, ProvedEntitlementInvalidation = 15,
    RiskyCredentialMutationAndDelegation = 16, ImplicatedPublication = 17, ImplicatedRegistration = 18,
    ImplicatedResourceAndRedirect = 19, UnsafeFormDefinitionAndSubmission = 20,
    AudienceDisclosureAndDerivatives = 21, ActualParticipantAgeEligibility = 22,
    ClassificationAndMaterialApproval = 23, CollectedSecretIncidentAccess = 24,
    NamedAutomationCreation = 25, ImplicatedCollection = 26
}

[JsonConverter(typeof(ConfigurationModerationEnumConverter<ConfigurationModerationCapability>))]
public enum ConfigurationModerationCapability
{
    Publish = 1, Upload = 2, ReadResource = 3, Manage = 4, Participate = 5, Admission = 6,
    PrivateLogistics = 7, Enrollment = 8, ProtectedStatus = 9, Contestation = 10, Support = 11, PrivacyErasure = 12
}

[JsonConverter(typeof(ConfigurationModerationEnumConverter<ConfigurationModerationTimingKind>))]
public enum ConfigurationModerationTimingKind
{
    NoEnforcementInterval = 1, Finite = 2, ReviewedIndefinite = 3, EarlierOfDayOrEventEnd = 4,
    FixedEventEnd = 5, ObjectReviewWithinDay = 6, RecoveryOrDayReviewBoundary = 7,
    CurrentControlUntilQualifiedResolution = 8, ProtectiveDayWithReview = 9
}

[JsonConverter(typeof(ConfigurationModerationEnumConverter<ConfigurationModerationBundle>))]
public enum ConfigurationModerationBundle
{
    Unaffected = 1, PublicationReview = 2, PublicationPause = 3, SelectedReadProtection = 4,
    ObjectQuarantine = 5, ManagementContinuity = 6, DigitalParticipation = 7,
    EventSafety = 8, CommunitySafety = 9, Protection = 10
}

[JsonConverter(typeof(ConfigurationModerationEnumConverter<ConfigurationModerationProtectionPurpose>))]
public enum ConfigurationModerationProtectionPurpose { EntitlementIntegrity = 1, CredentialRecovery = 2 }

[JsonConverter(typeof(ConfigurationModerationEnumConverter<ConfigurationModerationStrategy>))]
public enum ConfigurationModerationStrategy { PublicationIntegrity = 1, AgeAudienceEligibility = 2, ClassificationIntegrity = 3 }

[JsonConverter(typeof(ConfigurationModerationEnumConverter<ConfigurationModerationContentMode>))]
public enum ConfigurationModerationContentMode
{
    PreserveSafePublished = 1, PendingPublicationReview = 2, StopSelectedPublication = 3,
    QuarantineNamedVersion = 4, HoldOrQuarantineImplicatedVersions = 5,
    CurrentQualifiedAudienceProjection = 6, CorrectClassificationAndRevalidateVersions = 7
}

[JsonConverter(typeof(ConfigurationModerationEnumConverter<ConfigurationModerationManagementMode>))]
public enum ConfigurationModerationManagementMode
{
    CurrentAuthority = 1, DeniedEssentialOperationsContinuity = 2, OnlyVenueSensitiveOperationsContinuity = 3,
    PauseRiskyCredentialMutations = 4, QualifiedAdultStewardWhenRequired = 5
}

[JsonConverter(typeof(ConfigurationModerationEnumConverter<ConfigurationModerationResourceReadMode>))]
public enum ConfigurationModerationResourceReadMode
{
    CurrentAudience = 1, DenySelectedMetadataPayloadAndRedirect = 2, QuarantineNamedVersionSafeStatusOnly = 3,
    QualifiedSafeObligationAndSessionOnly = 4, SafeSeparatePurposeOrOwnedDerivative = 5,
    DenyBadEntitlementDerivedAccess = 6, GuardImplicatedVersionAndDestination = 7, CurrentQualifiedAudience = 8
}

[JsonConverter(typeof(ConfigurationModerationEnumConverter<ConfigurationModerationNewAdmissionMode>))]
public enum ConfigurationModerationNewAdmissionMode
{
    CurrentEligibility = 1, PauseOnlyUnresolvedEssentialStewardship = 2, DenySelectedOnlineJoinAndRegistration = 3,
    DenySelectedEventWindow = 4, DenySelectedCommunity = 5, DenySelectedIntegrityRegistration = 6,
    HoldImplicatedIntake = 7, CurrentQualifiedParticipantAge = 8
}

[JsonConverter(typeof(ConfigurationModerationEnumConverter<ConfigurationModerationExistingAdmissionMode>))]
public enum ConfigurationModerationExistingAdmissionMode
{
    CurrentEligibility = 1, SelectedParticipantHoldOrRevocation = 2, SelectedCommunityParticipantRevocation = 3,
    InvalidateProvedBadEntitlement = 4, NonDisciplinaryEligibilityHoldAndSupport = 5
}

[JsonConverter(typeof(ConfigurationModerationEnumConverter<ConfigurationModerationPrivateDisclosureMode>))]
public enum ConfigurationModerationPrivateDisclosureMode
{
    CurrentAudience = 1, DenyExactSelectedInformation = 2, MaskQuarantinedEmbeddedDetails = 3,
    PreserveValidBookingLogistics = 4, FenceSelectedVenueAndAllDerivatives = 5,
    FenceCommunityVenueAndAllDerivatives = 6, DenyProvedBadEntitlementDerivatives = 7,
    GuardImplicatedResponsesExportsAndDestinations = 8, QualifiedAudienceAtViewAndFinalDispatch = 9
}

[JsonConverter(typeof(ConfigurationModerationEnumConverter<ConfigurationModerationSettlementMode>))]
public enum ConfigurationModerationSettlementMode
{
    NoBookingChange = 1, AffectedParticipantNativeRights = 2, ImplicatedRightsAndOwnedIncident = 3, EligibilityHoldNativeRights = 4
}

[JsonConverter(typeof(ConfigurationModerationEnumConverter<ConfigurationModerationCapacityMode>))]
public enum ConfigurationModerationCapacityMode { NoSeatChange = 1, OwningParticipantTransitionExactlyOnce = 2 }

[JsonConverter(typeof(ConfigurationModerationEnumConverter<ConfigurationModerationCredentialMode>))]
public enum ConfigurationModerationCredentialMode
{
    CurrentCredentialLifecycle = 1, FenceSelectedParticipantAndRoster = 2, InvalidateProvedBadEntitlementsOnly = 3,
    VerifyRecoveryBeforeRelease = 4, RecheckActualParticipantEligibility = 5
}

[JsonConverter(typeof(ConfigurationModerationEnumConverter<ConfigurationModerationNotificationMode>))]
public enum ConfigurationModerationNotificationMode
{
    DurableSafeSubjectAndAdmin = 1, SafeAffectedParticipantAndIncident = 2, SafeEligibilityAndCorrection = 3
}

[JsonConverter(typeof(ConfigurationModerationEnumConverter<ConfigurationModerationRecognitionMode>))]
public enum ConfigurationModerationRecognitionMode { SeparatelyQualifiedFinalSanctionOnly = 1, NoCaptureFromThisControl = 2 }

[JsonConverter(typeof(ConfigurationModerationEnumConverter<ConfigurationModerationPendingWorkMode>))]
public enum ConfigurationModerationPendingWorkMode
{
    RecheckCurrentEffectAtUseAndDispatch = 1, CurrentPublicationApprovalRequired = 2,
    FreshAuthorityBeforeBlockedWorkResumes = 3, OwnedObjectReviewNeverAutomaticRelease = 4,
    OwnedContinuityDeadlineAndEscalation = 5, FencePromotionClaimTransferAndFinalization = 6,
    ExplicitRecoveryReviewAndRenewal = 7, MaterialVersionReviewAndIncidentOwner = 8,
    CurrentAgeAssuranceAndCorrection = 9, ClassificationReviewAndProjectionInvalidation = 10
}

[JsonConverter(typeof(ConfigurationModerationEnumConverter<ConfigurationModerationRestorationMode>))]
public enum ConfigurationModerationRestorationMode
{
    RecomputeCurrentRightsOnly = 1, CurrentAudienceAndAvailableResource = 2, AuthorizedObjectDecision = 3,
    FreshConsentAndAuthorityForHandoff = 4, CurrentRightsNeverReissueCancelledTickets = 5,
    VerifiedCredentialAndRoleRecovery = 6, QualifiedAgeCorrectionNoHistoryPenalty = 7, CurrentClassificationApproval = 8
}
