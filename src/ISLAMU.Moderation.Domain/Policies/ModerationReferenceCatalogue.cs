using System.Collections.Frozen;
using Basis = ISLAMU.Moderation.Domain.Policies.ModerationReferenceBasis;
using Bundle = ISLAMU.Moderation.Domain.Policies.ModerationConsequenceBundleKind;
using Effect = ISLAMU.Moderation.Domain.Policies.ModerationReferenceEffectKind;
using Grade = ISLAMU.Moderation.Domain.Policies.ModerationReferenceGrade;
using Ground = ISLAMU.Moderation.Domain.Policies.ModerationReferenceGround;
using Mode = ISLAMU.Moderation.Domain.Policies.ModerationReferenceEffectMode;
using Prerequisite = ISLAMU.Moderation.Domain.Policies.ModerationReferencePrerequisite;
using ProfileId = ISLAMU.Moderation.Domain.Policies.ModerationReferenceProfileId;
using Recognition = ISLAMU.Moderation.Domain.Policies.ModerationReferenceRecognition;
using Strategy = ISLAMU.Moderation.Domain.Policies.ModerationAdditiveStrategyKind;
using Target = ISLAMU.Moderation.Domain.Policies.ModerationReferenceTarget;
using Timing = ISLAMU.Moderation.Domain.Policies.ModerationReferenceTimingKind;

namespace ISLAMU.Moderation.Domain.Policies;

/// <summary>
/// R7 consultation section 18 and trust/youth extensions, narrowed by approved plan section 5.4.
/// Proposed immutable data only: native policy adoption, effect registration and terminal
/// conformance remain separate authorities. No chat/contact effects are represented.
/// </summary>
public static partial class ModerationReferenceCatalogue
{
    static ModerationReferenceCatalogue()
    {
        // Initialize profiles after both consequence inventories, including across partial files.
        Profiles = CreateProfiles().ToFrozenDictionary(profile => profile.Id);
    }

    public static IReadOnlyDictionary<ModerationReferenceProfileId, ModerationReferenceProfile> Profiles { get; }
    public static long Revision => 1;

    public static ModerationReferenceProfile GetProfile(ModerationReferenceProfileId id) =>
        Profiles.TryGetValue(id, out var profile) ? profile : throw new ArgumentOutOfRangeException(nameof(id));

    private static IEnumerable<ModerationReferenceProfile> CreateProfiles()
    {
        yield return Profile(ProfileId.CFG01, Ground.CompatibleConfirmedConduct, Basis.ConfirmedWarning, Bundle.Unaffected,
            effects: [Native(Effect.ReasonedNotice, Mode.Notice, Target.SelectedOperations)],
            scopes: AllScopes(), responses: [Response(Grade.G0, Timing.NoEnforcementInterval, 0)],
            recognition: Recognition.Never);
        yield return Profile(ProfileId.CFG05, Ground.PublicationMisuse, Basis.FinalConductFinding, Bundle.PublicationReview,
            effects: [Native(Effect.NewAndMaterialPublication, Mode.RequireReview, Target.SelectedOperations, ModerationCapability.Publish)],
            scopes: LocalScopes(), responses: [Response(Grade.G1, Timing.Finite, 1)],
            prerequisites: [Prerequisite.ActualPublicationReview]);
        yield return Profile(ProfileId.CFG06, Ground.PublicationAbuse, Basis.FinalConductFinding, Bundle.PublicationPause,
            effects: [Deny(Effect.NewAndMaterialPublication, Target.SelectedOperations, ModerationCapability.Publish)],
            scopes: LocalScopes(), responses: DigitalResponses());
        yield return Profile(ProfileId.CFG07, Ground.UploadMisuse, Basis.FinalConductFinding, Bundle.Unaffected,
            effects: [Deny(Effect.NewUploadAndResourcePublication, Target.SelectedOperations, ModerationCapability.Upload),
                Native(Effect.NewUploadAndResourcePublication, Mode.Deny, Target.SelectedOperations, ModerationCapability.Publish)],
            scopes: LocalScopes(), responses: [Response(Grade.G1, Timing.Finite, 1), .. DigitalResponses()],
            prerequisites: [Prerequisite.ExactTargetOperationPartition]);
        yield return Profile(ProfileId.CFG08, Ground.ConfidentialityOrAccessAbuse, Basis.FinalConductFinding, Bundle.SelectedReadProtection,
            effects: [Deny(Effect.SelectedResourceMetadataPayloadRedirect, Target.SelectedResourceVersion, ModerationCapability.ReadResource)],
            scopes: [ModerationScopeKind.Resource], responses: DigitalResponses(),
            prerequisites: [Prerequisite.ExactResourceAudienceAndVersion]);
        yield return Profile(ProfileId.CFG09, Ground.VerifiedUnsafeResource, Basis.VerifiedUnsafeObject, Bundle.ObjectQuarantine,
            effects: [Native(Effect.UnsafeResourceVersion, Mode.Quarantine, Target.SelectedResourceVersion, ModerationCapability.ReadResource)],
            scopes: [ModerationScopeKind.Resource], responses: [Response(Grade.Control, Timing.ObjectReviewWithinDay, 1)],
            prerequisites: [Prerequisite.ExactResourceAudienceAndVersion], recognition: Recognition.Never,
            subjects: [ModerationSubjectKind.Content]);
        yield return Profile(ProfileId.CFG10, Ground.EventManagementMisuse, Basis.FinalConductFinding, Bundle.ManagementContinuity,
            effects: [Native(Effect.SelectedEventManagement, Mode.Deny, Target.SelectedOperations, ModerationCapability.Manage)],
            scopes: [ModerationScopeKind.Resource], responses: DigitalResponses(),
            prerequisites: [Prerequisite.EssentialOperationFallbackSteward, Prerequisite.NativeSettlementOwnerAndRightsMapping,
                Prerequisite.ExactTargetOperationPartition]);
        yield return Profile(ProfileId.CFG11, Ground.PrivilegedTenantMisuse, Basis.FinalConductFinding, Bundle.ManagementContinuity,
            effects: [Native(Effect.SelectedTenantAdministration, Mode.Deny, Target.SelectedTenant, ModerationCapability.Manage)],
            scopes: [ModerationScopeKind.Tenant], responses: ExtendedResponses(2),
            prerequisites: [Prerequisite.EssentialOperationFallbackSteward, Prerequisite.NativeSettlementOwnerAndRightsMapping,
                Prerequisite.ExactTargetOperationPartition]);
        yield return Profile(ProfileId.CFG12, Ground.BroadTenantMisconduct, Basis.FinalConductFinding, Bundle.DigitalParticipation,
            effects: DigitalEffects(Target.SelectedTenant, enrollment: false),
            scopes: [ModerationScopeKind.Tenant], responses: ExtendedResponses(1),
            prerequisites: [Prerequisite.EssentialOperationFallbackSteward, Prerequisite.ExactTargetOperationPartition]);
        yield return Profile(ProfileId.CFG13, Ground.BroadSolutionMisconduct, Basis.FinalConductFinding, Bundle.DigitalParticipation,
            effects: DigitalEffects(Target.SelectedSolution, enrollment: true),
            scopes: [ModerationScopeKind.Solution], responses: ExtendedResponses(2),
            prerequisites: [Prerequisite.EssentialOperationFallbackSteward, Prerequisite.ExactTargetOperationPartition],
            recognition: Recognition.SeparatelyQualifiedFinalEnrollment);
        yield return Profile(ProfileId.CFG14, Ground.IndependentlyEstablishedCrossSolutionHarm, Basis.FinalConductFinding, Bundle.DigitalParticipation,
            effects: DigitalEffects(Target.EnrolledTrustDomain, enrollment: true),
            scopes: [ModerationScopeKind.TrustDomain], responses: ExtendedResponses(2),
            prerequisites: [Prerequisite.EnrolledSolutionMapping, Prerequisite.EssentialOperationFallbackSteward,
                Prerequisite.ExactTargetOperationPartition], recognition: Recognition.SeparatelyQualifiedFinalEnrollment);
        yield return Profile(ProfileId.CFG15, Ground.CredibleImminentPhysicalRisk, Basis.QualifiedUrgentProtection, Bundle.EventSafety,
            effects: PhysicalEffects(Target.SelectedParticipantEventWindow, Mode.Hold),
            scopes: [ModerationScopeKind.Resource], responses: [Response(Grade.G1, Timing.EarlierOfDayOrEventEnd, 1, followUp: true)],
            prerequisites: PhysicalPrerequisites(), recognition: Recognition.Never,
            subjects: [ModerationSubjectKind.Human]);
        yield return Profile(ProfileId.CFG16, Ground.IndependentlyConfirmedPhysicalMisconduct, Basis.FinalConductFinding, Bundle.EventSafety,
            effects: PhysicalEffects(Target.SelectedParticipantEventWindow, Mode.Revoke),
            scopes: [ModerationScopeKind.Resource],
            responses: [Response(Grade.G2, Timing.FixedEventEnd, 0), Response(Grade.G3, Timing.FixedEventEnd, 0)],
            prerequisites: PhysicalPrerequisites(), subjects: [ModerationSubjectKind.Human]);
        yield return Profile(ProfileId.CFG17, Ground.IndependentlyResolvedRecurrentPhysicalRisk, Basis.FinalConductFinding, Bundle.CommunitySafety,
            effects: PhysicalEffects(Target.SelectedCommunityParticipants, Mode.Revoke),
            scopes: [ModerationScopeKind.Community], responses: [Response(Grade.G3, Timing.Finite, 30, approvals: 2)],
            prerequisites: [.. PhysicalPrerequisites(), Prerequisite.IndependentlyResolvedRecurrentPhysicalIncidents],
            subjects: [ModerationSubjectKind.Human]);
        yield return Profile(ProfileId.CFG18, Ground.SeriousContinuedPhysicalRisk, Basis.FinalConductFinding, Bundle.CommunitySafety,
            effects: PhysicalEffects(Target.SelectedCommunityParticipants, Mode.Revoke),
            scopes: [ModerationScopeKind.Community], responses: [Response(Grade.G4, Timing.ReviewedIndefinite, 90, approvals: 2)],
            prerequisites: [.. PhysicalPrerequisites(), Prerequisite.IndependentlyResolvedRecurrentPhysicalIncidents],
            subjects: [ModerationSubjectKind.Human]);
        yield return Profile(ProfileId.CFG19, Ground.ProvedRegistrationOrTicketAbuse, Basis.FinalConductFinding, Bundle.Protection,
            effects: [Native(Effect.SelectedPhysicalRegistration, Mode.Deny, Target.SelectedOperations, ModerationCapability.Participate),
                Native(Effect.ProvedEntitlementInvalidation, Mode.Revoke, Target.ProvedBadEntitlements, ModerationCapability.Admission),
                Native(Effect.ProvedEntitlementInvalidation, Mode.Deny, Target.ProvedBadEntitlements, ModerationCapability.ReadResource),
                Native(Effect.ProvedEntitlementInvalidation, Mode.Deny, Target.ProvedBadEntitlements, ModerationCapability.PrivateLogistics)],
            scopes: [ModerationScopeKind.Tenant, ModerationScopeKind.Community, ModerationScopeKind.Resource],
            responses: DigitalResponses(),
            prerequisites: [Prerequisite.QualifiedParticipantBindingAndFinalScan, Prerequisite.NativeSettlementOwnerAndRightsMapping,
                Prerequisite.ExactTargetOperationPartition],
            protectionPurpose: ModerationProtectionPurpose.EntitlementIntegrity,
            subjects: [ModerationSubjectKind.Human]);
        yield return Profile(ProfileId.CFG20, Ground.CredibleCredentialCompromise, Basis.QualifiedUrgentProtection, Bundle.Protection,
            effects: [Native(Effect.RiskyCredentialMutationAndDelegation, Mode.Hold, Target.CompromisedCredentialOperations, ModerationCapability.Manage),
                Native(Effect.RiskyCredentialMutationAndDelegation, Mode.Hold, Target.CompromisedCredentialOperations, ModerationCapability.Publish),
                Native(Effect.RiskyCredentialMutationAndDelegation, Mode.Hold, Target.CompromisedCredentialOperations, ModerationCapability.Upload),
                Native(Effect.RiskyCredentialMutationAndDelegation, Mode.Hold, Target.CompromisedCredentialOperations, ModerationCapability.Participate),
                Native(Effect.RiskyCredentialMutationAndDelegation, Mode.Hold, Target.CompromisedCredentialOperations, ModerationCapability.Enrollment)],
            scopes: [ModerationScopeKind.Instance, ModerationScopeKind.Tenant],
            responses: [Response(Grade.G1, Timing.RecoveryOrDayReviewBoundary, 1, followUp: true)],
            prerequisites: [Prerequisite.VerifiedCredentialRecovery, Prerequisite.ExactTargetOperationPartition],
            recognition: Recognition.Never, protectionPurpose: ModerationProtectionPurpose.CredentialRecovery,
            subjects: [ModerationSubjectKind.Human, ModerationSubjectKind.Service]);
        yield return Profile(ProfileId.CFG23, Ground.ProvenAbusiveAutomation, Basis.FinalConductFinding, Bundle.PublicationPause,
            effects: [Native(Effect.NamedAutomationCreation, Mode.Deny, Target.SelectedOperations, ModerationCapability.Manage),
                Deny(Effect.NewAndMaterialPublication, Target.SelectedOperations, ModerationCapability.Publish)],
            scopes: LocalScopes(), responses: DigitalResponses(),
            prerequisites: [Prerequisite.ExactTargetOperationPartition],
            subjects: [ModerationSubjectKind.Human, ModerationSubjectKind.Service, ModerationSubjectKind.Organization]);
        yield return Profile(ProfileId.CFG24, Ground.CredibleDeceptivePublicationRisk, Basis.QualifiedUrgentProtection, Bundle.Unaffected,
            effects: [Native(Effect.ImplicatedPublication, Mode.Hold, Target.ImplicatedEventFormDestinationVersions, ModerationCapability.Publish),
                Native(Effect.ImplicatedCollection, Mode.Hold, Target.ImplicatedEventFormDestinationVersions),
                Native(Effect.ImplicatedRegistration, Mode.Hold, Target.ImplicatedEventFormDestinationVersions, ModerationCapability.Participate),
                Native(Effect.ImplicatedResourceAndRedirect, Mode.Hold, Target.ImplicatedEventFormDestinationVersions, ModerationCapability.ReadResource)],
            scopes: [ModerationScopeKind.Resource], responses: [Response(Grade.G1, Timing.ProtectiveDayWithReview, 1, followUp: true)],
            prerequisites: PublicationIntegrityPrerequisites(), recognition: Recognition.Never,
            strategies: [Strategy.PublicationIntegrity], subjects: [ModerationSubjectKind.Content]);
        yield return Profile(ProfileId.CFG25, Ground.ConfirmedDeceptiveOrProhibitedPublication, Basis.FinalConductFinding, Bundle.PublicationPause,
            effects: [Native(Effect.ImplicatedPublication, Mode.Deny, Target.ImplicatedEventFormDestinationVersions, ModerationCapability.Publish),
                Native(Effect.ImplicatedResourceAndRedirect, Mode.Quarantine, Target.ImplicatedEventFormDestinationVersions, ModerationCapability.ReadResource),
                Native(Effect.UnsafeFormDefinitionAndSubmission, Mode.Guard, Target.ImplicatedEventFormDestinationVersions),
                Native(Effect.CollectedSecretIncidentAccess, Mode.Guard, Target.ImplicatedEventFormDestinationVersions)],
            scopes: LocalScopes(), responses: DigitalResponses(),
            prerequisites: PublicationIntegrityPrerequisites(),
            strategies: [Strategy.PublicationIntegrity]);
        yield return Profile(ProfileId.CFG26, Ground.KnownProhibitedSecretSolicitation, Basis.KnownProhibitedDefinition, Bundle.Unaffected,
            effects: [Native(Effect.UnsafeFormDefinitionAndSubmission, Mode.Guard, Target.ImplicatedEventFormDestinationVersions)],
            scopes: [ModerationScopeKind.Resource], responses: [Response(Grade.Control, Timing.NoEnforcementInterval, 0)],
            prerequisites: PublicationIntegrityPrerequisites(), recognition: Recognition.Never,
            strategies: [Strategy.PublicationIntegrity], subjects: [ModerationSubjectKind.Content]);
        yield return Profile(ProfileId.CFG28, Ground.CurrentAdultAudienceEligibility, Basis.CurrentEligibility, Bundle.Unaffected,
            effects: [Native(Effect.AudienceDisclosureAndDerivatives, Mode.Guard, Target.ActualViewerAndParticipant, ModerationCapability.ReadResource),
                Native(Effect.ActualParticipantAgeEligibility, Mode.Guard, Target.ActualViewerAndParticipant, ModerationCapability.Participate),
                Native(Effect.ActualParticipantAgeEligibility, Mode.Guard, Target.ActualViewerAndParticipant, ModerationCapability.Admission)],
            scopes: [ModerationScopeKind.Resource], responses: [Response(Grade.Control, Timing.CurrentControlUntilQualifiedResolution, 0)],
            prerequisites: AgePrerequisites(), recognition: Recognition.Never,
            strategies: [Strategy.AgeAudienceEligibility], minimumAudienceAge: 18, subjects: [ModerationSubjectKind.Human]);
        yield return Profile(ProfileId.CFG29, Ground.ClassificationMismatchOrHonestError, Basis.ClassificationCorrection, Bundle.Unaffected,
            effects: [Native(Effect.ClassificationAndMaterialApproval, Mode.RequireReview, Target.ClassifiedContentVersions, ModerationCapability.Publish),
                Native(Effect.AudienceDisclosureAndDerivatives, Mode.Guard, Target.ClassifiedContentVersions, ModerationCapability.ReadResource)],
            scopes: [ModerationScopeKind.Resource], responses: [Response(Grade.Control, Timing.CurrentControlUntilQualifiedResolution, 0)],
            prerequisites: ClassificationPrerequisites(), recognition: Recognition.Never,
            strategies: [Strategy.ClassificationIntegrity], subjects: [ModerationSubjectKind.Content]);
        yield return Profile(ProfileId.CFG30, Ground.FirstFinalClassificationViolation, Basis.ConfirmedWarning, Bundle.Unaffected,
            effects: [Native(Effect.ReasonedNotice, Mode.Notice, Target.SelectedOperations),
                Native(Effect.ClassificationAndMaterialApproval, Mode.Correct, Target.ClassifiedContentVersions, ModerationCapability.Publish)],
            scopes: LocalScopes(), responses: [Response(Grade.G0, Timing.NoEnforcementInterval, 0)],
            prerequisites: ClassificationPrerequisites(progression: true), recognition: Recognition.Never,
            strategies: [Strategy.ClassificationIntegrity], qualifyingFindingOrdinal: 1, recurrenceWindow: TimeSpan.FromDays(180));
        yield return Profile(ProfileId.CFG31, Ground.SecondFinalClassificationViolation, Basis.FinalConductFinding, Bundle.PublicationReview,
            effects: [Native(Effect.NewAndMaterialPublication, Mode.RequireReview, Target.SelectedOperations, ModerationCapability.Publish)],
            scopes: LocalScopes(), responses: [Response(Grade.G2, Timing.Finite, 7)],
            prerequisites: [.. ClassificationPrerequisites(progression: true), Prerequisite.ActualPublicationReview],
            strategies: [Strategy.ClassificationIntegrity], qualifyingFindingOrdinal: 2, recurrenceWindow: TimeSpan.FromDays(180));
        yield return Profile(ProfileId.CFG32, Ground.ThirdFinalClassificationViolation, Basis.FinalConductFinding, Bundle.PublicationPause,
            effects: [Deny(Effect.NewAndMaterialPublication, Target.SelectedOperations, ModerationCapability.Publish)],
            scopes: LocalScopes(), responses: [Response(Grade.G2, Timing.Finite, 7)],
            prerequisites: ClassificationPrerequisites(progression: true),
            strategies: [Strategy.ClassificationIntegrity], qualifyingFindingOrdinal: 3, recurrenceWindow: TimeSpan.FromDays(180));
        yield return Profile(ProfileId.CFG33, Ground.FourthOrJustifiedPersistentClassificationEvasion, Basis.FinalConductFinding, Bundle.PublicationPause,
            effects: [Deny(Effect.NewAndMaterialPublication, Target.SelectedOperations, ModerationCapability.Publish)],
            scopes: LocalScopes(), responses: [Response(Grade.G3, Timing.Finite, 30)],
            prerequisites: ClassificationPrerequisites(progression: true),
            strategies: [Strategy.ClassificationIntegrity], qualifyingFindingOrdinal: 4, recurrenceWindow: TimeSpan.FromDays(180),
            persistentEvasionFindingMayReplaceOrdinal: true);
    }

    private static ModerationReferenceProfile Profile(
        ProfileId id, Ground ground, Basis basis, Bundle bundle, IEnumerable<ModerationReferenceEffect> effects,
        IEnumerable<ModerationScopeKind> scopes, IEnumerable<ModerationReferenceResponse> responses,
        IEnumerable<Prerequisite>? prerequisites = null, Recognition recognition = Recognition.SeparatelyQualifiedFinalAction,
        ModerationProtectionPurpose? protectionPurpose = null, IEnumerable<Strategy>? strategies = null,
        int? minimumAudienceAge = null, int? qualifyingFindingOrdinal = null, TimeSpan? recurrenceWindow = null,
        IEnumerable<ModerationSubjectKind>? subjects = null, bool persistentEvasionFindingMayReplaceOrdinal = false)
    {
        Prerequisite[] common = [Prerequisite.ExplicitPolicyAdoption, Prerequisite.CurrentNativeConformance,
            Prerequisite.CurrentOrdinaryAuthorization, Prerequisite.QualifiedSubjectAndTarget,
            Prerequisite.ProtectedRemedyRoutes, Prerequisite.QualifiedUnconflictedReviewers, Prerequisite.NoMandatoryExternalProvider];
        var required = common.Concat(prerequisites ?? []);
        if (basis is Basis.FinalConductFinding or Basis.ConfirmedWarning)
        {
            required = required.Concat([Prerequisite.CompatibleFinalFinding, Prerequisite.ResponseOpportunityOrQualifiedUrgency]);
        }

        if (basis == Basis.QualifiedUrgentProtection)
        {
            required = required.Concat([Prerequisite.ResponseOpportunityOrQualifiedUrgency, Prerequisite.IndependentProtectiveFollowUp]);
        }

        return new(id, ground, basis, bundle, protectionPurpose, effects, scopes,
            subjects ?? [ModerationSubjectKind.Human, ModerationSubjectKind.Service, ModerationSubjectKind.Organization],
            responses, required, recognition, strategies ?? [], minimumAudienceAge, qualifyingFindingOrdinal,
            recurrenceWindow, persistentEvasionFindingMayReplaceOrdinal);
    }

    private static ModerationReferenceEffect Deny(Effect kind, Target target, ModerationCapability capability) =>
        new(kind, Mode.Deny, target, capability, ModerationReferenceExecution.OverlayDenial);

    private static ModerationReferenceEffect Native(Effect kind, Mode mode, Target target, ModerationCapability? capability = null) =>
        new(kind, mode, target, capability, ModerationReferenceExecution.NativeOwner);

    private static ModerationReferenceResponse Response(
        Grade grade, Timing timing, int days, int approvals = 1, bool followUp = false) =>
        new(grade, new(timing, grade == Grade.G1 ? TimeSpan.FromHours(24) : TimeSpan.FromDays(days)), approvals, followUp);

    private static ModerationReferenceResponse[] DigitalResponses() =>
        [Response(Grade.G2, Timing.Finite, 7), Response(Grade.G3, Timing.Finite, 30)];

    private static ModerationReferenceResponse[] ExtendedResponses(int finiteApprovals) =>
        [Response(Grade.G3, Timing.Finite, 30, finiteApprovals), Response(Grade.G4, Timing.ReviewedIndefinite, 90, approvals: 2)];

    private static ModerationScopeKind[] LocalScopes() =>
        [ModerationScopeKind.Instance, ModerationScopeKind.Tenant, ModerationScopeKind.Community, ModerationScopeKind.Resource];

    private static ModerationScopeKind[] AllScopes() =>
        [ModerationScopeKind.TrustDomain, ModerationScopeKind.Solution, .. LocalScopes()];

    private static ModerationReferenceEffect[] DigitalEffects(Target target, bool enrollment) =>
        [Deny(Effect.NewAndMaterialPublication, target, ModerationCapability.Publish),
            Deny(Effect.NewUploadAndResourcePublication, target, ModerationCapability.Upload),
            Native(Effect.DigitalJoinAndNewOnlineRegistration, Mode.Deny, target, ModerationCapability.Participate),
            Native(Effect.DigitalJoinAndNewOnlineRegistration, Mode.Deny, target, ModerationCapability.Enrollment),
            .. (enrollment ? new[] { Deny(Effect.ProductEnrollment, target, ModerationCapability.Enrollment) } : [])];

    private static ModerationReferenceEffect[] PhysicalEffects(Target target, Mode disposition) =>
        [new(Effect.ParticipantAdmission, disposition, target, ModerationCapability.Admission, ModerationReferenceExecution.OverlayDenial),
            Deny(Effect.SelectedPhysicalRegistration, target, ModerationCapability.Participate),
            Deny(Effect.PrivateVenueAndDerivedDisclosure, target, ModerationCapability.PrivateLogistics),
            Native(Effect.VenueSensitiveManagement, Mode.Deny, target, ModerationCapability.Manage),
            Native(Effect.VenueSensitiveResourceReads, Mode.Deny, target, ModerationCapability.ReadResource)];

    private static Prerequisite[] PhysicalPrerequisites() =>
        [Prerequisite.QualifiedParticipantBindingAndFinalScan, Prerequisite.DisclosureAndDerivativeFences,
            Prerequisite.NativeSettlementOwnerAndRightsMapping, Prerequisite.EssentialOperationFallbackSteward,
            Prerequisite.ExactTargetOperationPartition];

    private static Prerequisite[] PublicationIntegrityPrerequisites() =>
        [Prerequisite.PurposeAndMaterialVersionReview, Prerequisite.ProhibitedSecretNoStorageOrEcho,
            Prerequisite.OwnedPrivacySecurityIncident, Prerequisite.NativeSettlementOwnerAndRightsMapping,
            Prerequisite.ExactTargetOperationPartition];

    private static Prerequisite[] AgePrerequisites() =>
        [Prerequisite.AcceptedAgeAssurance, Prerequisite.LawfulPrivateAgeAuthorityAndCorrection,
            Prerequisite.CompleteAudienceAndParticipantCoverage, Prerequisite.WholeEventFloorUnlessProvedSessionSegregation,
            Prerequisite.NativeSettlementOwnerAndRightsMapping, Prerequisite.DisclosureAndDerivativeFences];

    private static Prerequisite[] ClassificationPrerequisites(bool progression = false) =>
        [Prerequisite.AcceptedClassificationPolicyAndHumanReview,
            Prerequisite.PurposeAndMaterialVersionReview, Prerequisite.CompleteAudienceAndParticipantCoverage,
            Prerequisite.DisclosureAndDerivativeFences,
            .. (progression ? new[] { Prerequisite.DistinctFinalKnownPolicyFindings } : [])];
}
