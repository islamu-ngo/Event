using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Explore.Domain;

/// <summary>
/// Immutable native publication and identity-bound completion receipt. Definitions are retained
/// even when unsupported. Active records certify only the measured publication instant; operation
/// writers must still acquire current deployment, consumer, prerequisite and target authority.
/// </summary>
public sealed class ModerationPolicyPublication
{
    public required Guid Id { get; init; }
    public required Guid PolicyId { get; init; }
    public required long Revision { get; init; }
    /// <summary>Null for a non-authorizing definition snapshot; adopted publications retain their exact revision.</summary>
    public required long? AdoptedRevision { get; init; }
    public required Guid? PreviousPublicationId { get; init; }
    public required Guid HostBindingId { get; init; }
    public required Guid TrustDomainId { get; init; }
    public required Guid SolutionId { get; init; }
    public required Guid InstanceId { get; init; }
    public required Guid? JurisdictionTenantId { get; init; }
    public required Guid TenantId { get; init; }
    public required Guid PrincipalId { get; init; }
    public required Guid OperationId { get; init; }
    public required string RequestDigest { get; init; }
    public required string DeclaredStateDigest { get; init; }
    public required Guid AuthoritySourceId { get; init; }
    public required long AuthorityRevision { get; init; }
    public required long QualificationRevision { get; init; }
    public required string WriterGeneration { get; init; }
    public required long CatalogueRevision { get; init; }
    public required long InventoryRevision { get; init; }
    public required string InventoryFingerprint { get; init; }
    public required bool PublishedActive { get; init; }
    public required DateTimeOffset PublishedAt { get; init; }
    public required DateTimeOffset? EvidenceValidUntil { get; init; }
    public required long MaximumFiniteDurationTicks { get; init; }
    public required long MaximumReviewDelayTicks { get; init; }
    public required int MinimumApprovals { get; init; }
    public required int MaximumApprovals { get; init; }
    public required int MinimumContestationCalendarMonths { get; init; }

    private List<ModerationPolicyPublicationClass> _classes = [];
    private List<ModerationPolicyPublicationReason> _reasons = [];
    private List<ModerationPolicyPublicationProfile> _profiles = [];
    private List<ModerationPolicyPublicationMode> _modes = [];
    private List<ModerationPolicyPublicationEvidence> _evidence = [];
    public required IReadOnlyList<ModerationPolicyPublicationClass> Classes
    { get => _classes.AsReadOnly(); init => _classes = value.ToList(); }
    public required IReadOnlyList<ModerationPolicyPublicationReason> Reasons
    { get => _reasons.AsReadOnly(); init => _reasons = value.ToList(); }
    public required IReadOnlyList<ModerationPolicyPublicationProfile> Profiles
    { get => _profiles.AsReadOnly(); init => _profiles = value.ToList(); }
    public required IReadOnlyList<ModerationPolicyPublicationMode> Modes
    { get => _modes.AsReadOnly(); init => _modes = value.ToList(); }
    public required IReadOnlyList<ModerationPolicyPublicationEvidence> Evidence
    { get => _evidence.AsReadOnly(); init => _evidence = value.ToList(); }

    public AuthorityScope Jurisdiction
    {
        get
        {
            var instance = AuthorityScope.Instance(
                AuthorityScope.Solution(AuthorityScope.TrustDomain(TrustDomainId), SolutionId), InstanceId);
            return JurisdictionTenantId is { } tenant ? AuthorityScope.Tenant(instance, tenant) : instance;
        }
    }

    public override string ToString() => nameof(ModerationPolicyPublication);
}

public sealed class ModerationPolicyPublicationClass
{
    public required Guid PublicationId { get; init; }
    public required string Code { get; init; }
    public required long Revision { get; init; }
    public required string Meaning { get; init; }
    public required ModerationReferenceGround Ground { get; init; }
    public required ModerationReferenceBasis Basis { get; init; }
}

public sealed class ModerationPolicyPublicationReason
{
    public required Guid PublicationId { get; init; }
    public required string Code { get; init; }
    public required long Revision { get; init; }
    public required string Meaning { get; init; }
    public required string ClassCode { get; init; }
    public required long ClassRevision { get; init; }
}

public sealed class ModerationPolicyPublicationMode
{
    public required Guid PublicationId { get; init; }
    /// <summary>The finite native deployment mode ordinal, not a plugin identifier.</summary>
    public required int Mode { get; init; }
}

/// <summary>
/// The exact immutable profile identity and thirteen-dimensional base/additive plans.
/// Scope and subject rows are the selected definition's exact sets. Native authoring permits
/// support only below its actual jurisdiction and within the finite shipped effect universe.
/// Deployment approval is separately required for active publication.
/// </summary>
public sealed class ModerationPolicyPublicationProfile
{
    public required Guid PublicationId { get; init; }
    public required Guid ProfileId { get; init; }
    public required string RuleCode { get; init; }
    public required long RuleRevision { get; init; }
    public required string ReasonCode { get; init; }
    public required long ReasonRevision { get; init; }
    public required ModerationReferenceProfileId ReferenceProfile { get; init; }
    public required long ReferenceRevision { get; init; }
    public required ModerationReferenceGrade Grade { get; init; }
    public required ModerationConsequenceBundleKind Bundle { get; init; }
    public required ModerationProtectionPurpose? ProtectionPurpose { get; init; }
    public required ModerationReferenceTimingKind TimingKind { get; init; }
    public required long DurationTicks { get; init; }
    public required int RequiredApprovals { get; init; }
    public required int ContestationCalendarMonths { get; init; }
    public required ModerationReferenceRecognition Recognition { get; init; }
    public required bool Supported { get; init; }
    private List<ModerationPolicyPublicationScope> _scopes = [];
    private List<ModerationPolicyPublicationSubject> _subjects = [];
    private List<ModerationPolicyPublicationEffect> _effects = [];
    private List<ModerationPolicyPublicationPlan> _plans = [];
    private List<ModerationPolicyPublicationConsumer> _consumers = [];
    private List<ModerationPolicyPublicationPrerequisite> _prerequisites = [];
    public required IReadOnlyList<ModerationPolicyPublicationScope> Scopes
    { get => _scopes.AsReadOnly(); init => _scopes = value.ToList(); }
    public required IReadOnlyList<ModerationPolicyPublicationSubject> Subjects
    { get => _subjects.AsReadOnly(); init => _subjects = value.ToList(); }
    public required IReadOnlyList<ModerationPolicyPublicationEffect> Effects
    { get => _effects.AsReadOnly(); init => _effects = value.ToList(); }
    public required IReadOnlyList<ModerationPolicyPublicationPlan> Plans
    { get => _plans.AsReadOnly(); init => _plans = value.ToList(); }
    public required IReadOnlyList<ModerationPolicyPublicationConsumer> Consumers
    { get => _consumers.AsReadOnly(); init => _consumers = value.ToList(); }
    public required IReadOnlyList<ModerationPolicyPublicationPrerequisite> Prerequisites
    { get => _prerequisites.AsReadOnly(); init => _prerequisites = value.ToList(); }
}

public sealed class ModerationPolicyPublicationScope
{
    public required Guid PublicationId { get; init; }
    public required Guid ProfileId { get; init; }
    public required ModerationScopeKind Kind { get; init; }
}

public sealed class ModerationPolicyPublicationSubject
{
    public required Guid PublicationId { get; init; }
    public required Guid ProfileId { get; init; }
    public required ModerationSubjectKind Kind { get; init; }
}

public sealed class ModerationPolicyPublicationEffect
{
    public required Guid PublicationId { get; init; }
    public required Guid ProfileId { get; init; }
    public required int Ordinal { get; init; }
    public required ModerationReferenceEffectKind Kind { get; init; }
    public required ModerationReferenceEffectMode Mode { get; init; }
    public required ModerationReferenceTarget Target { get; init; }
    public required ModerationCapability? Capability { get; init; }
    public required ModerationReferenceExecution Execution { get; init; }
    public required int SemanticOwner { get; init; }
    public ModerationReferenceEffect Meaning => new(Kind, Mode, Target, Capability, Execution);
}

public sealed class ModerationPolicyPublicationPlan
{
    public required Guid PublicationId { get; init; }
    public required Guid ProfileId { get; init; }
    /// <summary>Zero is the base plan; positive values are finite additive strategy ordinals.</summary>
    public required int Strategy { get; init; }
    public required ModerationContentMode Content { get; init; }
    public required ModerationManagementMode Management { get; init; }
    public required ModerationResourceReadMode Resources { get; init; }
    public required ModerationNewAdmissionMode NewAdmission { get; init; }
    public required ModerationExistingAdmissionMode ExistingAdmission { get; init; }
    public required ModerationPrivateDisclosureMode PrivateDisclosure { get; init; }
    public required ModerationSettlementMode Settlement { get; init; }
    public required ModerationCapacityMode Capacity { get; init; }
    public required ModerationCredentialMode Credentials { get; init; }
    public required ModerationNotificationMode Notifications { get; init; }
    public required ModerationRecognitionMode Recognition { get; init; }
    public required ModerationPendingWorkMode PendingWork { get; init; }
    public required ModerationRestorationMode Restoration { get; init; }
    public ModerationConsequenceModes Meaning => new(Content, Management, Resources, NewAdmission,
        ExistingAdmission, PrivateDisclosure, Settlement, Capacity, Credentials, Notifications,
        Recognition, PendingWork, Restoration);
}

public sealed class ModerationPolicyPublicationConsumer
{
    public required Guid PublicationId { get; init; }
    public required Guid ProfileId { get; init; }
    public required int Mode { get; init; }
    public required ModerationCapability Capability { get; init; }
    public required string ConsumerId { get; init; }
    public required string SubjectResolver { get; init; }
    public required string TargetResolver { get; init; }
    public required string AuthorizationOwner { get; init; }
    public required string EntryCheckpoint { get; init; }
    public required string TerminalCheckpoint { get; init; }
    public required string WriterFence { get; init; }
    public required int FreshnessClass { get; init; }
    public required string OwningPacket { get; init; }
    public required string ConformanceTest { get; init; }
}

public sealed class ModerationPolicyPublicationPrerequisite
{
    public required Guid PublicationId { get; init; }
    public required Guid ProfileId { get; init; }
    public required int Mode { get; init; }
    /// <summary>Exactly one of the two typed prerequisite families is populated.</summary>
    public required int Ordinal { get; init; }
    public required int? ActivationGate { get; init; }
    public required ModerationReferencePrerequisite? NativePrerequisite { get; init; }
}

/// <summary>Retained provenance, never permission for a later operation or publication.</summary>
public sealed class ModerationPolicyPublicationEvidence
{
    public required Guid PublicationId { get; init; }
    public required string RequirementDigest { get; init; }
    public required Guid? ApprovalId { get; init; }
    public required long? ApprovalRevision { get; init; }
    public required Guid SourceId { get; init; }
    public required long SourceRevision { get; init; }
    public required string WriterGeneration { get; init; }
    public required DateTimeOffset ObservedAt { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}
