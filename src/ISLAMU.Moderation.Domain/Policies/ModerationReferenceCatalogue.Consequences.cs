using System.Collections.Frozen;

namespace ISLAMU.Moderation.Domain.Policies;

public static partial class ModerationReferenceCatalogue
{
    public static IReadOnlyDictionary<ModerationConsequenceBundleKind, ModerationReferenceBundle> Bundles { get; } =
        CreateBundles().ToFrozenDictionary(bundle => bundle.Kind);

    public static IReadOnlyDictionary<ModerationAdditiveStrategyKind, ModerationConsequenceModes> Strategies { get; } =
        new Dictionary<ModerationAdditiveStrategyKind, ModerationConsequenceModes>
        {
            [ModerationAdditiveStrategyKind.PublicationIntegrity] = new(
                Content: ModerationContentMode.HoldOrQuarantineImplicatedVersions,
                Management: ModerationManagementMode.CurrentAuthority,
                Resources: ModerationResourceReadMode.GuardImplicatedVersionAndDestination,
                NewAdmission: ModerationNewAdmissionMode.HoldImplicatedIntake,
                ExistingAdmission: ModerationExistingAdmissionMode.CurrentEligibility,
                PrivateDisclosure: ModerationPrivateDisclosureMode.GuardImplicatedResponsesExportsAndDestinations,
                Settlement: ModerationSettlementMode.ImplicatedRightsAndOwnedIncident,
                Capacity: ModerationCapacityMode.OwningParticipantTransitionExactlyOnce,
                Credentials: ModerationCredentialMode.CurrentCredentialLifecycle,
                Notifications: ModerationNotificationMode.SafeAffectedParticipantAndIncident,
                Recognition: ModerationRecognitionMode.NoCaptureFromThisControl,
                PendingWork: ModerationPendingWorkMode.MaterialVersionReviewAndIncidentOwner,
                Restoration: ModerationRestorationMode.AuthorizedObjectDecision),
            [ModerationAdditiveStrategyKind.AgeAudienceEligibility] = new(
                Content: ModerationContentMode.CurrentQualifiedAudienceProjection,
                Management: ModerationManagementMode.QualifiedAdultStewardWhenRequired,
                Resources: ModerationResourceReadMode.CurrentQualifiedAudience,
                NewAdmission: ModerationNewAdmissionMode.CurrentQualifiedParticipantAge,
                ExistingAdmission: ModerationExistingAdmissionMode.NonDisciplinaryEligibilityHoldAndSupport,
                PrivateDisclosure: ModerationPrivateDisclosureMode.QualifiedAudienceAtViewAndFinalDispatch,
                Settlement: ModerationSettlementMode.EligibilityHoldNativeRights,
                Capacity: ModerationCapacityMode.OwningParticipantTransitionExactlyOnce,
                Credentials: ModerationCredentialMode.RecheckActualParticipantEligibility,
                Notifications: ModerationNotificationMode.SafeEligibilityAndCorrection,
                Recognition: ModerationRecognitionMode.NoCaptureFromThisControl,
                PendingWork: ModerationPendingWorkMode.CurrentAgeAssuranceAndCorrection,
                Restoration: ModerationRestorationMode.QualifiedAgeCorrectionNoHistoryPenalty),
            [ModerationAdditiveStrategyKind.ClassificationIntegrity] = new(
                Content: ModerationContentMode.CorrectClassificationAndRevalidateVersions,
                Management: ModerationManagementMode.CurrentAuthority,
                Resources: ModerationResourceReadMode.CurrentQualifiedAudience,
                NewAdmission: ModerationNewAdmissionMode.CurrentQualifiedParticipantAge,
                ExistingAdmission: ModerationExistingAdmissionMode.CurrentEligibility,
                PrivateDisclosure: ModerationPrivateDisclosureMode.QualifiedAudienceAtViewAndFinalDispatch,
                Settlement: ModerationSettlementMode.NoBookingChange,
                Capacity: ModerationCapacityMode.NoSeatChange,
                Credentials: ModerationCredentialMode.CurrentCredentialLifecycle,
                Notifications: ModerationNotificationMode.SafeEligibilityAndCorrection,
                Recognition: ModerationRecognitionMode.NoCaptureFromThisControl,
                PendingWork: ModerationPendingWorkMode.ClassificationReviewAndProjectionInvalidation,
                Restoration: ModerationRestorationMode.CurrentClassificationApproval)
        }.ToFrozenDictionary();

    public static ModerationReferenceBundle GetBundle(ModerationConsequenceBundleKind kind) =>
        Bundles.TryGetValue(kind, out var bundle) ? bundle : throw new ArgumentOutOfRangeException(nameof(kind));

    public static ModerationConsequenceModes GetStrategy(ModerationAdditiveStrategyKind kind) =>
        Strategies.TryGetValue(kind, out var strategy) ? strategy : throw new ArgumentOutOfRangeException(nameof(kind));

    /// <summary>An already due handoff is immediately escalated; it never silently changes the physical event.</summary>
    public static DateTimeOffset ContinuityDeadline(DateTimeOffset activatedAt, DateTimeOffset eventStartsAt)
    {
        var day = activatedAt.AddHours(24);
        var beforeEvent = eventStartsAt.AddHours(-2);
        var deadline = day < beforeEvent ? day : beforeEvent;
        return (deadline < activatedAt ? activatedAt : deadline).ToUniversalTime();
    }

    private static IEnumerable<ModerationReferenceBundle> CreateBundles()
    {
        yield return new(ModerationConsequenceBundleKind.Unaffected, new(
            Content: ModerationContentMode.PreserveSafePublished,
            Management: ModerationManagementMode.CurrentAuthority,
            Resources: ModerationResourceReadMode.CurrentAudience,
            NewAdmission: ModerationNewAdmissionMode.CurrentEligibility,
            ExistingAdmission: ModerationExistingAdmissionMode.CurrentEligibility,
            PrivateDisclosure: ModerationPrivateDisclosureMode.CurrentAudience,
            Settlement: ModerationSettlementMode.NoBookingChange,
            Capacity: ModerationCapacityMode.NoSeatChange,
            Credentials: ModerationCredentialMode.CurrentCredentialLifecycle,
            Notifications: ModerationNotificationMode.DurableSafeSubjectAndAdmin,
            Recognition: ModerationRecognitionMode.SeparatelyQualifiedFinalSanctionOnly,
            PendingWork: ModerationPendingWorkMode.RecheckCurrentEffectAtUseAndDispatch,
            Restoration: ModerationRestorationMode.RecomputeCurrentRightsOnly));
        yield return new(ModerationConsequenceBundleKind.PublicationReview, new(
            Content: ModerationContentMode.PendingPublicationReview,
            Management: ModerationManagementMode.CurrentAuthority,
            Resources: ModerationResourceReadMode.CurrentAudience,
            NewAdmission: ModerationNewAdmissionMode.CurrentEligibility,
            ExistingAdmission: ModerationExistingAdmissionMode.CurrentEligibility,
            PrivateDisclosure: ModerationPrivateDisclosureMode.CurrentAudience,
            Settlement: ModerationSettlementMode.NoBookingChange,
            Capacity: ModerationCapacityMode.NoSeatChange,
            Credentials: ModerationCredentialMode.CurrentCredentialLifecycle,
            Notifications: ModerationNotificationMode.DurableSafeSubjectAndAdmin,
            Recognition: ModerationRecognitionMode.SeparatelyQualifiedFinalSanctionOnly,
            PendingWork: ModerationPendingWorkMode.CurrentPublicationApprovalRequired,
            Restoration: ModerationRestorationMode.RecomputeCurrentRightsOnly));
        yield return new(ModerationConsequenceBundleKind.PublicationPause, new(
            Content: ModerationContentMode.StopSelectedPublication,
            Management: ModerationManagementMode.CurrentAuthority,
            Resources: ModerationResourceReadMode.CurrentAudience,
            NewAdmission: ModerationNewAdmissionMode.CurrentEligibility,
            ExistingAdmission: ModerationExistingAdmissionMode.CurrentEligibility,
            PrivateDisclosure: ModerationPrivateDisclosureMode.CurrentAudience,
            Settlement: ModerationSettlementMode.NoBookingChange,
            Capacity: ModerationCapacityMode.NoSeatChange,
            Credentials: ModerationCredentialMode.CurrentCredentialLifecycle,
            Notifications: ModerationNotificationMode.DurableSafeSubjectAndAdmin,
            Recognition: ModerationRecognitionMode.SeparatelyQualifiedFinalSanctionOnly,
            PendingWork: ModerationPendingWorkMode.FreshAuthorityBeforeBlockedWorkResumes,
            Restoration: ModerationRestorationMode.RecomputeCurrentRightsOnly));
        yield return new(ModerationConsequenceBundleKind.SelectedReadProtection, new(
            Content: ModerationContentMode.PreserveSafePublished,
            Management: ModerationManagementMode.CurrentAuthority,
            Resources: ModerationResourceReadMode.DenySelectedMetadataPayloadAndRedirect,
            NewAdmission: ModerationNewAdmissionMode.CurrentEligibility,
            ExistingAdmission: ModerationExistingAdmissionMode.CurrentEligibility,
            PrivateDisclosure: ModerationPrivateDisclosureMode.DenyExactSelectedInformation,
            Settlement: ModerationSettlementMode.NoBookingChange,
            Capacity: ModerationCapacityMode.NoSeatChange,
            Credentials: ModerationCredentialMode.CurrentCredentialLifecycle,
            Notifications: ModerationNotificationMode.DurableSafeSubjectAndAdmin,
            Recognition: ModerationRecognitionMode.SeparatelyQualifiedFinalSanctionOnly,
            PendingWork: ModerationPendingWorkMode.RecheckCurrentEffectAtUseAndDispatch,
            Restoration: ModerationRestorationMode.CurrentAudienceAndAvailableResource));
        yield return new(ModerationConsequenceBundleKind.ObjectQuarantine, new(
            Content: ModerationContentMode.QuarantineNamedVersion,
            Management: ModerationManagementMode.CurrentAuthority,
            Resources: ModerationResourceReadMode.QuarantineNamedVersionSafeStatusOnly,
            NewAdmission: ModerationNewAdmissionMode.CurrentEligibility,
            ExistingAdmission: ModerationExistingAdmissionMode.CurrentEligibility,
            PrivateDisclosure: ModerationPrivateDisclosureMode.MaskQuarantinedEmbeddedDetails,
            Settlement: ModerationSettlementMode.NoBookingChange,
            Capacity: ModerationCapacityMode.NoSeatChange,
            Credentials: ModerationCredentialMode.CurrentCredentialLifecycle,
            Notifications: ModerationNotificationMode.DurableSafeSubjectAndAdmin,
            Recognition: ModerationRecognitionMode.NoCaptureFromThisControl,
            PendingWork: ModerationPendingWorkMode.OwnedObjectReviewNeverAutomaticRelease,
            Restoration: ModerationRestorationMode.AuthorizedObjectDecision));
        yield return new(ModerationConsequenceBundleKind.ManagementContinuity, new(
            Content: ModerationContentMode.PreserveSafePublished,
            Management: ModerationManagementMode.DeniedEssentialOperationsContinuity,
            Resources: ModerationResourceReadMode.CurrentAudience,
            NewAdmission: ModerationNewAdmissionMode.PauseOnlyUnresolvedEssentialStewardship,
            ExistingAdmission: ModerationExistingAdmissionMode.CurrentEligibility,
            PrivateDisclosure: ModerationPrivateDisclosureMode.CurrentAudience,
            Settlement: ModerationSettlementMode.NoBookingChange,
            Capacity: ModerationCapacityMode.NoSeatChange,
            Credentials: ModerationCredentialMode.CurrentCredentialLifecycle,
            Notifications: ModerationNotificationMode.DurableSafeSubjectAndAdmin,
            Recognition: ModerationRecognitionMode.SeparatelyQualifiedFinalSanctionOnly,
            PendingWork: ModerationPendingWorkMode.OwnedContinuityDeadlineAndEscalation,
            Restoration: ModerationRestorationMode.FreshConsentAndAuthorityForHandoff));
        yield return new(ModerationConsequenceBundleKind.DigitalParticipation, new(
            Content: ModerationContentMode.StopSelectedPublication,
            Management: ModerationManagementMode.DeniedEssentialOperationsContinuity,
            Resources: ModerationResourceReadMode.QualifiedSafeObligationAndSessionOnly,
            NewAdmission: ModerationNewAdmissionMode.DenySelectedOnlineJoinAndRegistration,
            ExistingAdmission: ModerationExistingAdmissionMode.CurrentEligibility,
            PrivateDisclosure: ModerationPrivateDisclosureMode.PreserveValidBookingLogistics,
            Settlement: ModerationSettlementMode.NoBookingChange,
            Capacity: ModerationCapacityMode.NoSeatChange,
            Credentials: ModerationCredentialMode.CurrentCredentialLifecycle,
            Notifications: ModerationNotificationMode.DurableSafeSubjectAndAdmin,
            Recognition: ModerationRecognitionMode.SeparatelyQualifiedFinalSanctionOnly,
            PendingWork: ModerationPendingWorkMode.FreshAuthorityBeforeBlockedWorkResumes,
            Restoration: ModerationRestorationMode.CurrentRightsNeverReissueCancelledTickets));
        yield return new(ModerationConsequenceBundleKind.EventSafety, new(
            Content: ModerationContentMode.PreserveSafePublished,
            Management: ModerationManagementMode.OnlyVenueSensitiveOperationsContinuity,
            Resources: ModerationResourceReadMode.SafeSeparatePurposeOrOwnedDerivative,
            NewAdmission: ModerationNewAdmissionMode.DenySelectedEventWindow,
            ExistingAdmission: ModerationExistingAdmissionMode.SelectedParticipantHoldOrRevocation,
            PrivateDisclosure: ModerationPrivateDisclosureMode.FenceSelectedVenueAndAllDerivatives,
            Settlement: ModerationSettlementMode.AffectedParticipantNativeRights,
            Capacity: ModerationCapacityMode.OwningParticipantTransitionExactlyOnce,
            Credentials: ModerationCredentialMode.FenceSelectedParticipantAndRoster,
            Notifications: ModerationNotificationMode.SafeAffectedParticipantAndIncident,
            Recognition: ModerationRecognitionMode.SeparatelyQualifiedFinalSanctionOnly,
            PendingWork: ModerationPendingWorkMode.FencePromotionClaimTransferAndFinalization,
            Restoration: ModerationRestorationMode.CurrentRightsNeverReissueCancelledTickets));
        yield return new(ModerationConsequenceBundleKind.CommunitySafety, new(
            Content: ModerationContentMode.PreserveSafePublished,
            Management: ModerationManagementMode.OnlyVenueSensitiveOperationsContinuity,
            Resources: ModerationResourceReadMode.SafeSeparatePurposeOrOwnedDerivative,
            NewAdmission: ModerationNewAdmissionMode.DenySelectedCommunity,
            ExistingAdmission: ModerationExistingAdmissionMode.SelectedCommunityParticipantRevocation,
            PrivateDisclosure: ModerationPrivateDisclosureMode.FenceCommunityVenueAndAllDerivatives,
            Settlement: ModerationSettlementMode.AffectedParticipantNativeRights,
            Capacity: ModerationCapacityMode.OwningParticipantTransitionExactlyOnce,
            Credentials: ModerationCredentialMode.FenceSelectedParticipantAndRoster,
            Notifications: ModerationNotificationMode.SafeAffectedParticipantAndIncident,
            Recognition: ModerationRecognitionMode.SeparatelyQualifiedFinalSanctionOnly,
            PendingWork: ModerationPendingWorkMode.FencePromotionClaimTransferAndFinalization,
            Restoration: ModerationRestorationMode.CurrentRightsNeverReissueCancelledTickets));
        yield return new(
            integrity: new(
                Content: ModerationContentMode.PreserveSafePublished,
                Management: ModerationManagementMode.CurrentAuthority,
                Resources: ModerationResourceReadMode.DenyBadEntitlementDerivedAccess,
                NewAdmission: ModerationNewAdmissionMode.DenySelectedIntegrityRegistration,
                ExistingAdmission: ModerationExistingAdmissionMode.InvalidateProvedBadEntitlement,
                PrivateDisclosure: ModerationPrivateDisclosureMode.DenyProvedBadEntitlementDerivatives,
                Settlement: ModerationSettlementMode.AffectedParticipantNativeRights,
                Capacity: ModerationCapacityMode.OwningParticipantTransitionExactlyOnce,
                Credentials: ModerationCredentialMode.InvalidateProvedBadEntitlementsOnly,
                Notifications: ModerationNotificationMode.SafeAffectedParticipantAndIncident,
                Recognition: ModerationRecognitionMode.SeparatelyQualifiedFinalSanctionOnly,
                PendingWork: ModerationPendingWorkMode.FencePromotionClaimTransferAndFinalization,
                Restoration: ModerationRestorationMode.CurrentRightsNeverReissueCancelledTickets),
            recovery: new(
                Content: ModerationContentMode.PreserveSafePublished,
                Management: ModerationManagementMode.PauseRiskyCredentialMutations,
                Resources: ModerationResourceReadMode.CurrentAudience,
                NewAdmission: ModerationNewAdmissionMode.CurrentEligibility,
                ExistingAdmission: ModerationExistingAdmissionMode.CurrentEligibility,
                PrivateDisclosure: ModerationPrivateDisclosureMode.CurrentAudience,
                Settlement: ModerationSettlementMode.NoBookingChange,
                Capacity: ModerationCapacityMode.NoSeatChange,
                Credentials: ModerationCredentialMode.VerifyRecoveryBeforeRelease,
                Notifications: ModerationNotificationMode.DurableSafeSubjectAndAdmin,
                Recognition: ModerationRecognitionMode.NoCaptureFromThisControl,
                PendingWork: ModerationPendingWorkMode.ExplicitRecoveryReviewAndRenewal,
                Restoration: ModerationRestorationMode.VerifiedCredentialAndRoleRecovery));
    }
}
