namespace ISLAMU.Moderation.Domain.Policies;

public enum ModerationConsequenceBundleKind
{
    Unaffected = 1,
    PublicationReview = 2,
    PublicationPause = 3,
    SelectedReadProtection = 4,
    ObjectQuarantine = 5,
    ManagementContinuity = 6,
    DigitalParticipation = 7,
    EventSafety = 8,
    CommunitySafety = 9,
    Protection = 10
}

public enum ModerationProtectionPurpose { EntitlementIntegrity = 1, CredentialRecovery = 2 }
public enum ModerationAdditiveStrategyKind { PublicationIntegrity = 1, AgeAudienceEligibility = 2, ClassificationIntegrity = 3 }
public enum ModerationContentMode
{
    PreserveSafePublished = 1, PendingPublicationReview = 2, StopSelectedPublication = 3,
    QuarantineNamedVersion = 4, HoldOrQuarantineImplicatedVersions = 5,
    CurrentQualifiedAudienceProjection = 6, CorrectClassificationAndRevalidateVersions = 7
}

public enum ModerationManagementMode
{
    CurrentAuthority = 1, DeniedEssentialOperationsContinuity = 2,
    OnlyVenueSensitiveOperationsContinuity = 3, PauseRiskyCredentialMutations = 4,
    QualifiedAdultStewardWhenRequired = 5
}

public enum ModerationResourceReadMode
{
    CurrentAudience = 1, DenySelectedMetadataPayloadAndRedirect = 2,
    QuarantineNamedVersionSafeStatusOnly = 3, QualifiedSafeObligationAndSessionOnly = 4,
    SafeSeparatePurposeOrOwnedDerivative = 5, DenyBadEntitlementDerivedAccess = 6,
    GuardImplicatedVersionAndDestination = 7, CurrentQualifiedAudience = 8
}

public enum ModerationNewAdmissionMode
{
    CurrentEligibility = 1, PauseOnlyUnresolvedEssentialStewardship = 2, DenySelectedOnlineJoinAndRegistration = 3,
    DenySelectedEventWindow = 4, DenySelectedCommunity = 5, DenySelectedIntegrityRegistration = 6,
    HoldImplicatedIntake = 7, CurrentQualifiedParticipantAge = 8
}

public enum ModerationExistingAdmissionMode
{
    CurrentEligibility = 1, SelectedParticipantHoldOrRevocation = 2,
    SelectedCommunityParticipantRevocation = 3, InvalidateProvedBadEntitlement = 4,
    NonDisciplinaryEligibilityHoldAndSupport = 5
}

public enum ModerationPrivateDisclosureMode
{
    CurrentAudience = 1, DenyExactSelectedInformation = 2, MaskQuarantinedEmbeddedDetails = 3,
    PreserveValidBookingLogistics = 4, FenceSelectedVenueAndAllDerivatives = 5,
    FenceCommunityVenueAndAllDerivatives = 6, DenyProvedBadEntitlementDerivatives = 7,
    GuardImplicatedResponsesExportsAndDestinations = 8, QualifiedAudienceAtViewAndFinalDispatch = 9
}

public enum ModerationSettlementMode
{
    NoBookingChange = 1, AffectedParticipantNativeRights = 2,
    ImplicatedRightsAndOwnedIncident = 3, EligibilityHoldNativeRights = 4
}

public enum ModerationCapacityMode { NoSeatChange = 1, OwningParticipantTransitionExactlyOnce = 2 }
public enum ModerationCredentialMode
{
    CurrentCredentialLifecycle = 1, FenceSelectedParticipantAndRoster = 2,
    InvalidateProvedBadEntitlementsOnly = 3, VerifyRecoveryBeforeRelease = 4,
    RecheckActualParticipantEligibility = 5
}

public enum ModerationNotificationMode { DurableSafeSubjectAndAdmin = 1, SafeAffectedParticipantAndIncident = 2, SafeEligibilityAndCorrection = 3 }
public enum ModerationRecognitionMode { SeparatelyQualifiedFinalSanctionOnly = 1, NoCaptureFromThisControl = 2 }
public enum ModerationPendingWorkMode
{
    RecheckCurrentEffectAtUseAndDispatch = 1, CurrentPublicationApprovalRequired = 2,
    FreshAuthorityBeforeBlockedWorkResumes = 3, OwnedObjectReviewNeverAutomaticRelease = 4,
    OwnedContinuityDeadlineAndEscalation = 5, FencePromotionClaimTransferAndFinalization = 6,
    ExplicitRecoveryReviewAndRenewal = 7, MaterialVersionReviewAndIncidentOwner = 8,
    CurrentAgeAssuranceAndCorrection = 9, ClassificationReviewAndProjectionInvalidation = 10
}

public enum ModerationRestorationMode
{
    RecomputeCurrentRightsOnly = 1, CurrentAudienceAndAvailableResource = 2,
    AuthorizedObjectDecision = 3, FreshConsentAndAuthorityForHandoff = 4,
    CurrentRightsNeverReissueCancelledTickets = 5, VerifiedCredentialAndRoleRecovery = 6,
    QualifiedAgeCorrectionNoHistoryPenalty = 7, CurrentClassificationApproval = 8
}

public enum ModerationOperationComposition { SameTargetDenialThenReviewThenCurrentPermission = 1 }
public enum ModerationResourceComposition { NamedVersionQuarantineOrReadDenialMasksRelease = 1 }
public enum ModerationParticipantComposition { SameParticipantWindowRevocationThenHoldThenCurrentEligibility = 1 }

/// <summary>Fixed reference invariants shared by every base and additive plan; none grants ordinary access.</summary>
public sealed record ModerationConsequenceSafeguards
{
    internal ModerationConsequenceSafeguards() { }
    public bool OrdinaryPermissionRequired => true;
    public bool PreserveIndependentPeopleRolesAndOrigin => true;
    public bool RecheckProhibitedEffectsAtUseAndDispatch => true;
    public bool PreserveProtectedRemedies => true;
    public bool PreserveQualifiedSettlementRoutes => true;
    public bool RecomputeRemainingContributions => true;
    public bool AutomaticHistoricalRestoration => false;
    public bool CanRecallDisclosedCopies => false;
    public bool DeduplicateByOwningTargetTransition => true;
    public bool IrreversibleCauseConflictRequiresOwningResolution => true;
    public bool PreserveEachContributionScopeAndInterval => true;
    public bool PreserveBothProtectivePurposes => true;
    public bool ContinuityOnlyForActuallyDeniedEssentialOperations => true;
    public bool RescheduleRequiresNewAuthorityToExtend => true;
    public ModerationOperationComposition Operations => ModerationOperationComposition.SameTargetDenialThenReviewThenCurrentPermission;
    public ModerationResourceComposition Resources => ModerationResourceComposition.NamedVersionQuarantineOrReadDenialMasksRelease;
    public ModerationParticipantComposition Participants => ModerationParticipantComposition.SameParticipantWindowRevocationThenHoldThenCurrentEligibility;
}

/// <summary>
/// Complete domain-owned handler modes. Additive plans remain separate contributions;
/// a native compiler must resolve same-target conflicts, not replace plans by grade.
/// </summary>
public sealed record ModerationConsequenceModes(
    ModerationContentMode Content,
    ModerationManagementMode Management,
    ModerationResourceReadMode Resources,
    ModerationNewAdmissionMode NewAdmission,
    ModerationExistingAdmissionMode ExistingAdmission,
    ModerationPrivateDisclosureMode PrivateDisclosure,
    ModerationSettlementMode Settlement,
    ModerationCapacityMode Capacity,
    ModerationCredentialMode Credentials,
    ModerationNotificationMode Notifications,
    ModerationRecognitionMode Recognition,
    ModerationPendingWorkMode PendingWork,
    ModerationRestorationMode Restoration)
{
    public ModerationConsequenceSafeguards Safeguards { get; } = new();
}

/// <summary>One of ten base bundles. Protection cannot be resolved without one of its two explicit purposes.</summary>
public sealed record ModerationReferenceBundle
{
    private readonly ModerationConsequenceModes? _ordinary;
    private readonly ModerationConsequenceModes? _integrity;
    private readonly ModerationConsequenceModes? _recovery;

    internal ModerationReferenceBundle(ModerationConsequenceBundleKind kind, ModerationConsequenceModes ordinary)
    {
        Kind = kind;
        _ordinary = ordinary;
    }

    internal ModerationReferenceBundle(ModerationConsequenceModes integrity, ModerationConsequenceModes recovery)
    {
        Kind = ModerationConsequenceBundleKind.Protection;
        _integrity = integrity;
        _recovery = recovery;
    }

    public ModerationConsequenceBundleKind Kind { get; }

    public ModerationConsequenceModes Resolve(ModerationProtectionPurpose? purpose = null)
    {
        if (Kind != ModerationConsequenceBundleKind.Protection)
        {
            if (purpose is not null)
            {
                throw new ArgumentException("Only the protective bundle accepts a protection purpose.", nameof(purpose));
            }

            return _ordinary!;
        }

        return purpose switch
        {
            ModerationProtectionPurpose.EntitlementIntegrity => _integrity!,
            ModerationProtectionPurpose.CredentialRecovery => _recovery!,
            null => throw new ArgumentException("The protective bundle requires an explicit purpose.", nameof(purpose)),
            _ => throw new ArgumentOutOfRangeException(nameof(purpose))
        };
    }
}
