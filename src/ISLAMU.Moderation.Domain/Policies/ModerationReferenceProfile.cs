using System.Collections.Frozen;

namespace ISLAMU.Moderation.Domain.Policies;

/// <summary>Original consultation identifiers; gaps are deliberately excluded chat/contact profiles.</summary>
public enum ModerationReferenceProfileId
{
    CFG01 = 1, CFG05 = 5, CFG06 = 6, CFG07 = 7, CFG08 = 8, CFG09 = 9, CFG10 = 10,
    CFG11 = 11, CFG12 = 12, CFG13 = 13, CFG14 = 14, CFG15 = 15, CFG16 = 16,
    CFG17 = 17, CFG18 = 18, CFG19 = 19, CFG20 = 20, CFG23 = 23, CFG24 = 24,
    CFG25 = 25, CFG26 = 26, CFG28 = 28, CFG29 = 29, CFG30 = 30, CFG31 = 31,
    CFG32 = 32, CFG33 = 33
}

public enum ModerationReferenceGrade { Control = -1, G0 = 0, G1 = 1, G2 = 2, G3 = 3, G4 = 4 }
public enum ModerationReferenceBasis
{
    ConfirmedWarning = 1, FinalConductFinding = 2, QualifiedUrgentProtection = 3,
    VerifiedUnsafeObject = 4, KnownProhibitedDefinition = 5, CurrentEligibility = 6,
    ClassificationCorrection = 7
}

public enum ModerationReferenceGround
{
    CompatibleConfirmedConduct = 1, PublicationMisuse = 2, PublicationAbuse = 3,
    UploadMisuse = 4, ConfidentialityOrAccessAbuse = 5, VerifiedUnsafeResource = 6,
    EventManagementMisuse = 7, PrivilegedTenantMisuse = 8, BroadTenantMisconduct = 9,
    BroadSolutionMisconduct = 10, IndependentlyEstablishedCrossSolutionHarm = 11,
    CredibleImminentPhysicalRisk = 12, IndependentlyConfirmedPhysicalMisconduct = 13,
    IndependentlyResolvedRecurrentPhysicalRisk = 14, SeriousContinuedPhysicalRisk = 15,
    ProvedRegistrationOrTicketAbuse = 16, CredibleCredentialCompromise = 17,
    ProvenAbusiveAutomation = 18, CredibleDeceptivePublicationRisk = 19,
    ConfirmedDeceptiveOrProhibitedPublication = 20, KnownProhibitedSecretSolicitation = 21,
    CurrentAdultAudienceEligibility = 22, ClassificationMismatchOrHonestError = 23,
    FirstFinalClassificationViolation = 24, SecondFinalClassificationViolation = 25,
    ThirdFinalClassificationViolation = 26, FourthOrJustifiedPersistentClassificationEvasion = 27
}

public enum ModerationReferenceRecognition { Never = 1, SeparatelyQualifiedFinalAction = 2, SeparatelyQualifiedFinalEnrollment = 3 }
public enum ModerationReferenceExecution { OverlayDenial = 1, NativeOwner = 2 }
public enum ModerationReferenceEffectMode { Notice = 1, Deny = 2, RequireReview = 3, Quarantine = 4, Guard = 5, Correct = 6, Hold = 7, Revoke = 8 }
public enum ModerationReferenceTarget
{
    SelectedOperations = 1, SelectedResourceVersion = 2, SelectedTenant = 3,
    SelectedSolution = 4, EnrolledTrustDomain = 5, SelectedParticipantEventWindow = 6,
    SelectedCommunityParticipants = 7, ProvedBadEntitlements = 8, CompromisedCredentialOperations = 9,
    ImplicatedEventFormDestinationVersions = 10, ActualViewerAndParticipant = 11,
    ClassifiedContentVersions = 12
}

public enum ModerationReferenceEffectKind
{
    ReasonedNotice = 1,
    NewAndMaterialPublication = 2,
    NewUploadAndResourcePublication = 3,
    SelectedResourceMetadataPayloadRedirect = 4,
    UnsafeResourceVersion = 5,
    SelectedEventManagement = 6,
    SelectedTenantAdministration = 7,
    DigitalJoinAndNewOnlineRegistration = 8,
    ProductEnrollment = 9,
    ParticipantAdmission = 10,
    SelectedPhysicalRegistration = 11,
    PrivateVenueAndDerivedDisclosure = 12,
    VenueSensitiveManagement = 13,
    VenueSensitiveResourceReads = 14,
    ProvedEntitlementInvalidation = 15,
    RiskyCredentialMutationAndDelegation = 16,
    ImplicatedPublication = 17,
    ImplicatedRegistration = 18,
    ImplicatedResourceAndRedirect = 19,
    UnsafeFormDefinitionAndSubmission = 20,
    AudienceDisclosureAndDerivatives = 21,
    ActualParticipantAgeEligibility = 22,
    ClassificationAndMaterialApproval = 23,
    CollectedSecretIncidentAccess = 24,
    NamedAutomationCreation = 25,
    ImplicatedCollection = 26
}

public enum ModerationReferencePrerequisite
{
    ExplicitPolicyAdoption = 1, CurrentNativeConformance = 2, CurrentOrdinaryAuthorization = 3,
    QualifiedSubjectAndTarget = 4, ProtectedRemedyRoutes = 5, QualifiedUnconflictedReviewers = 6,
    CompatibleFinalFinding = 7, ResponseOpportunityOrQualifiedUrgency = 8,
    ActualPublicationReview = 9, ExactResourceAudienceAndVersion = 10,
    EssentialOperationFallbackSteward = 11, NativeSettlementOwnerAndRightsMapping = 12,
    QualifiedParticipantBindingAndFinalScan = 13, DisclosureAndDerivativeFences = 14,
    IndependentProtectiveFollowUp = 15, IndependentlyResolvedRecurrentPhysicalIncidents = 16,
    EnrolledSolutionMapping = 17, VerifiedCredentialRecovery = 18,
    PurposeAndMaterialVersionReview = 19, ProhibitedSecretNoStorageOrEcho = 20,
    OwnedPrivacySecurityIncident = 21, AcceptedAgeAssurance = 22,
    LawfulPrivateAgeAuthorityAndCorrection = 23, CompleteAudienceAndParticipantCoverage = 24,
    WholeEventFloorUnlessProvedSessionSegregation = 25, DistinctFinalKnownPolicyFindings = 26,
    AcceptedClassificationPolicyAndHumanReview = 27, NoMandatoryExternalProvider = 28,
    ExplicitRecognitionPurposeKeysAndRetention = 29, ExactTargetOperationPartition = 30
}

public enum ModerationReferenceTimingKind
{
    NoEnforcementInterval = 1, Finite = 2, ReviewedIndefinite = 3,
    EarlierOfDayOrEventEnd = 4, FixedEventEnd = 5, ObjectReviewWithinDay = 6,
    RecoveryOrDayReviewBoundary = 7, CurrentControlUntilQualifiedResolution = 8,
    ProtectiveDayWithReview = 9
}

/// <summary>Bound reference timing, never a runtime authority or a reschedule-sensitive event pointer.</summary>
public sealed record ModerationReferenceInterval(
    ModerationReferenceTimingKind Kind, DateTimeOffset StartsAt, DateTimeOffset? EndsAt,
    DateTimeOffset? ReviewAt, DateTimeOffset? EventWindowEnd)
{
    public bool IsEffectiveAt(DateTimeOffset now) =>
        Kind != ModerationReferenceTimingKind.NoEnforcementInterval
        && now >= StartsAt && (EndsAt is null || now < EndsAt);
}

public sealed record ModerationReferenceTiming
{
    internal ModerationReferenceTiming(ModerationReferenceTimingKind kind, TimeSpan duration)
    {
        Kind = kind;
        Duration = duration;
    }

    public ModerationReferenceTimingKind Kind { get; }
    public TimeSpan Duration { get; }

    /// <summary>Pure reference binding. Recovery/reversal still requires a separate qualified native transition.</summary>
    public ModerationReferenceInterval Bind(DateTimeOffset startsAt, DateTimeOffset? eventWindowEnd = null)
    {
        startsAt = startsAt.ToUniversalTime();
        eventWindowEnd = eventWindowEnd?.ToUniversalTime();
        if (Kind is ModerationReferenceTimingKind.EarlierOfDayOrEventEnd or ModerationReferenceTimingKind.FixedEventEnd
            && (eventWindowEnd is null || eventWindowEnd <= startsAt))
        {
            throw new ArgumentException("Event timing requires the selected future window end at commit.", nameof(eventWindowEnd));
        }

        return Kind switch
        {
            ModerationReferenceTimingKind.NoEnforcementInterval => new(Kind, startsAt, null, null, null),
            ModerationReferenceTimingKind.Finite => new(Kind, startsAt, startsAt + Duration, null, null),
            ModerationReferenceTimingKind.ReviewedIndefinite => new(Kind, startsAt, null, startsAt + Duration, null),
            ModerationReferenceTimingKind.EarlierOfDayOrEventEnd => EventHold(startsAt, eventWindowEnd!.Value),
            ModerationReferenceTimingKind.FixedEventEnd => new(Kind, startsAt, eventWindowEnd, null, eventWindowEnd),
            ModerationReferenceTimingKind.ObjectReviewWithinDay => new(Kind, startsAt, null, startsAt + Duration, null),
            ModerationReferenceTimingKind.RecoveryOrDayReviewBoundary => new(Kind, startsAt, startsAt + Duration, startsAt + Duration, null),
            ModerationReferenceTimingKind.ProtectiveDayWithReview => new(Kind, startsAt, startsAt + Duration, startsAt + Duration, null),
            ModerationReferenceTimingKind.CurrentControlUntilQualifiedResolution => new(Kind, startsAt, null, null, null),
            _ => throw new InvalidOperationException("The reference timing kind is not supported.")
        };
    }

    private ModerationReferenceInterval EventHold(DateTimeOffset startsAt, DateTimeOffset end)
    {
        var bound = end < startsAt + Duration ? end : startsAt + Duration;
        return new(Kind, startsAt, bound, bound, end);
    }
}

public sealed record ModerationReferenceResponse(
    ModerationReferenceGrade Grade, ModerationReferenceTiming Timing, int RequiredApprovals,
    bool IndependentFollowUpRequired);

/// <summary>
/// Capability is an envelope under the existing enum, not proof that its finer operation is supported.
/// Native-owner controls cannot be flattened into an EffectiveRestrictions denial.
/// </summary>
public sealed record ModerationReferenceEffect(
    ModerationReferenceEffectKind Kind, ModerationReferenceEffectMode Mode,
    ModerationReferenceTarget Target, ModerationCapability? Capability, ModerationReferenceExecution Execution);

/// <summary>
/// Immutable proposed reference data. Selection neither adopts policy nor establishes findings,
/// authority, current consumer coverage, identity recognition or ordinary permission.
/// </summary>
public sealed record ModerationReferenceProfile
{
    internal ModerationReferenceProfile(
        ModerationReferenceProfileId id, ModerationReferenceGround ground, ModerationReferenceBasis basis,
        ModerationConsequenceBundleKind bundle, ModerationProtectionPurpose? protectionPurpose,
        IEnumerable<ModerationReferenceEffect> effects, IEnumerable<ModerationScopeKind> scopeKinds,
        IEnumerable<ModerationSubjectKind> subjectKinds, IEnumerable<ModerationReferenceResponse> responses,
        IEnumerable<ModerationReferencePrerequisite> prerequisites, ModerationReferenceRecognition recognition,
        IEnumerable<ModerationAdditiveStrategyKind> strategies, int? minimumAudienceAge,
        int? qualifyingFindingOrdinal, TimeSpan? recurrenceWindow, bool persistentEvasionFindingMayReplaceOrdinal)
    {
        Id = id;
        Ground = ground;
        Basis = basis;
        Bundle = bundle;
        ProtectionPurpose = protectionPurpose;
        Effects = Array.AsReadOnly(effects.ToArray());
        ScopeKinds = scopeKinds.ToFrozenSet();
        SubjectKinds = subjectKinds.ToFrozenSet();
        Responses = Array.AsReadOnly(responses.ToArray());
        Prerequisites = prerequisites.ToFrozenSet();
        Recognition = recognition;
        RecognitionPrerequisites = (recognition == ModerationReferenceRecognition.Never
            ? Array.Empty<ModerationReferencePrerequisite>()
            : [ModerationReferencePrerequisite.ExplicitRecognitionPurposeKeysAndRetention]).ToFrozenSet();
        Strategies = strategies.ToFrozenSet();
        MinimumAudienceAge = minimumAudienceAge;
        QualifyingFindingOrdinal = qualifyingFindingOrdinal;
        RecurrenceWindow = recurrenceWindow;
        PersistentEvasionFindingMayReplaceOrdinal = persistentEvasionFindingMayReplaceOrdinal;
        CapabilityEnvelope = Effects.Where(effect => effect.Capability is not null)
            .Select(effect => effect.Capability!.Value).ToFrozenSet();
        OverlayDenials = Effects.Where(effect => effect.Execution == ModerationReferenceExecution.OverlayDenial)
            .Select(effect => effect.Capability!.Value).ToFrozenSet();
        ConsequencePlans = Array.AsReadOnly(new[]
        {
            ModerationReferenceCatalogue.GetBundle(bundle).Resolve(protectionPurpose)
        }.Concat(Strategies.Order().Select(ModerationReferenceCatalogue.GetStrategy)).ToArray());
    }

    public ModerationReferenceProfileId Id { get; }
    public string Code => $"CFG{(int)Id:00}";
    public long Revision => 1;
    public ModerationReferenceBasis Basis { get; }
    public ModerationReferenceGround Ground { get; }
    public ModerationConsequenceBundleKind Bundle { get; }
    public ModerationProtectionPurpose? ProtectionPurpose { get; }
    public IReadOnlyList<ModerationReferenceEffect> Effects { get; }
    public IReadOnlySet<ModerationCapability> CapabilityEnvelope { get; }
    public IReadOnlySet<ModerationCapability> OverlayDenials { get; }
    public IReadOnlySet<ModerationScopeKind> ScopeKinds { get; }
    public IReadOnlySet<ModerationSubjectKind> SubjectKinds { get; }
    public IReadOnlyList<ModerationReferenceResponse> Responses { get; }
    public IReadOnlySet<ModerationReferencePrerequisite> Prerequisites { get; }
    public ModerationReferenceRecognition Recognition { get; }
    public IReadOnlySet<ModerationReferencePrerequisite> RecognitionPrerequisites { get; }
    public IReadOnlySet<ModerationAdditiveStrategyKind> Strategies { get; }
    public IReadOnlyList<ModerationConsequenceModes> ConsequencePlans { get; }
    public int? MinimumAudienceAge { get; }
    public int? QualifyingFindingOrdinal { get; }
    public TimeSpan? RecurrenceWindow { get; }
    public bool PersistentEvasionFindingMayReplaceOrdinal { get; }

    /// <summary>Reference scope shape only; current issuer permission and exact target binding remain native obligations.</summary>
    public bool AcceptsScope(AuthorityScope issuer, AuthorityScope target) =>
        issuer.Contains(target) && ScopeKinds.Contains(target.Kind);

    public ModerationReferenceResponse GetResponse(ModerationReferenceGrade grade) =>
        Responses.SingleOrDefault(response => response.Grade == grade)
        ?? throw new ArgumentOutOfRangeException(nameof(grade), "The grade is not configured for this profile.");
}
