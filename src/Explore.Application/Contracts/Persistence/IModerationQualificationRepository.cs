using Explore.Application.Services.Moderation;
using Explore.Domain;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IModerationQualificationRepository
{
    /// <summary>
    /// Requalifies finite native control-plane authority, mutates the actual source and
    /// its epoch atomically, and retains an exact identity/request-bound receipt.
    /// </summary>
    Task<ModerationQualificationWriteResult> ExecuteAsync(
        ModerationQualificationMutation mutation, CancellationToken cancellationToken = default);
}

public enum ModerationQualificationWriteOutcome
{
    Known = 1,
    Unauthenticated = 2,
    Denied = 3,
    Conflict = 4,
    NotFound = 5,
    Unavailable = 6
}

public sealed record ModerationQualificationWriteResult(
    ModerationQualificationWriteOutcome Outcome,
    ModerationQualificationMutationReceipt? Receipt = null);

/// <summary>Target and expected generations are intent, never caller or jurisdiction proof.</summary>
public abstract record ModerationQualificationMutation
{
    public required Guid OperationId { get; init; }
    public required AuthorityScope Target { get; init; }
    public required long ExpectedQualificationRevision { get; init; }
    public sealed override string ToString() => nameof(ModerationQualificationMutation);
}

public sealed record OpenModerationCaseSource : ModerationQualificationMutation
{
    public required Guid CaseId { get; init; }
    public required ModerationSubjectKind SubjectKind { get; init; }
    public required Guid SubjectId { get; init; }
    public required Guid OccurrenceId { get; init; }
}

/// <summary>Prepares measurements against native artifacts; contains no results or passing assertion.</summary>
public sealed record PrepareModerationConsumerMeasurementSource : ModerationQualificationMutation
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ModerationOriginalEffectAnchor? OriginalEffect { get; init; }
    public required Guid ChallengeId { get; init; }
    public required Guid InstallationId { get; init; }
    public required long ExpectedInstallationRevision { get; init; }
    public required string ConsumerId { get; init; }
    public required ModerationActivationBinding Binding { get; init; }
    public required string ExpectedRuntimeGeneration { get; init; }
    public required string ExpectedInventoryFingerprint { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

/// <summary>Retains an independently authenticated assertion without declaring conformance or permission.</summary>
public sealed record AcceptModerationConsumerMeasurementSource : ModerationQualificationMutation
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ModerationOriginalEffectAnchor? OriginalEffect { get; init; }
    public required Guid ReceiptId { get; init; }
    public required Guid ChallengeId { get; init; }
    public required Guid IssuerEnrollmentId { get; init; }
    public required ModerationActivationBinding Binding { get; init; }
    public required string SignedToken { get; init; }
}

/// <summary>Accountable deployment approval of public signer trust, never a conformance assertion.</summary>
public sealed record EnrollModerationMeasurementIssuerSource : ModerationQualificationMutation
{
    public required Guid EnrollmentId { get; init; }
    public required Guid KeyId { get; init; }
    public required string Issuer { get; init; }
    public required string PublicKeySpkiBase64 { get; init; }
    public required string RunnerDigest { get; init; }
    public required string CatalogueDigest { get; init; }
    public required string TestBundleDigest { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record RevokeModerationMeasurementIssuerSource : ModerationQualificationMutation
{
    public required Guid EnrollmentId { get; init; }
    public required long ExpectedEnrollmentRevision { get; init; }
}

public sealed record RecordModerationCaseResponse : ModerationQualificationMutation
{
    public required Guid EvidenceId { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required string Statement { get; init; }
}

public sealed record AssignModerationReviewerSource : ModerationQualificationMutation
{
    public required Guid AssignmentId { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required Guid ReviewerHumanId { get; init; }
}

public sealed record QualifyModerationReviewerController : ModerationQualificationMutation
{
    public required Guid AssignmentId { get; init; }
    public required long ExpectedAssignmentRevision { get; init; }
    public required Guid ControllerHumanId { get; init; }
    public required Guid EvidenceId { get; init; }
    public required string Justification { get; init; }
    public required DateTimeOffset QualifiedUntil { get; init; }
}

public sealed record AssessModerationReviewerConflict : ModerationQualificationMutation
{
    public required Guid AssignmentId { get; init; }
    public required long ExpectedAssignmentRevision { get; init; }
    public required ModerationReviewerConflictState State { get; init; }
    public required Guid EvidenceId { get; init; }
    public required string Justification { get; init; }
}

public sealed record WithdrawModerationReviewerSource : ModerationQualificationMutation
{
    public required Guid AssignmentId { get; init; }
    public required long ExpectedAssignmentRevision { get; init; }
}

public sealed record QualifyModerationCaseUrgency : ModerationQualificationMutation
{
    public required Guid EvidenceId { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required string Justification { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record FinalizeModerationFindingSource : ModerationQualificationMutation
{
    public required Guid FindingId { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required Guid PolicyId { get; init; }
    public required long ExpectedPolicyRevision { get; init; }
    public required ModerationRuleReference Rule { get; init; }
    public required ModerationRuleReference Reason { get; init; }
    public required Guid EvidenceId { get; init; }
    public required string Justification { get; init; }
}

public sealed record ReverseModerationFindingSource : ModerationQualificationMutation
{
    public required Guid FindingId { get; init; }
    public required long ExpectedFindingRevision { get; init; }
    public required Guid ReversalReferenceId { get; init; }
    public required string Justification { get; init; }
}

public sealed record PublishModerationQualificationPolicySource : ModerationQualificationMutation
{
    public required Guid SourceId { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    /// <summary>Zero creates the stable source; a positive revision replaces its current terms.</summary>
    public required long ExpectedSourceRevision { get; init; }
    public required TimeSpan MaximumReviewerLifetime { get; init; }
    public required TimeSpan MaximumUrgencyLifetime { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record AdoptModerationQualificationPolicySource : ModerationQualificationMutation
{
    public required Guid SourceId { get; init; }
    public required long ExpectedSourceRevision { get; init; }
    public required long ExpectedWriterGeneration { get; init; }
    public required Guid AdoptionId { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record RevokeModerationQualificationPolicyAdoption : ModerationQualificationMutation
{
    public required Guid SourceId { get; init; }
    public required long ExpectedSourceRevision { get; init; }
    public required Guid AdoptionId { get; init; }
    public required long ExpectedAdoptionRevision { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
}

public sealed record ApproveModerationNoticeDisclosureSource : ModerationQualificationMutation
{
    public required ModerationNoticeDisclosureIntent Intent { get; init; }
}

public sealed record RevokeModerationNoticeDisclosureSource : ModerationQualificationMutation
{
    public required Guid ApprovalId { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required long ExpectedApprovalRevision { get; init; }
}

/// <summary>Captures real fenced event ancestry; observation is not target adoption.</summary>
public sealed record RecordModerationEventGenerationSource : ModerationQualificationMutation
{
    public required Guid SourceId { get; init; }
    public required Guid EventId { get; init; }
    public required long ExpectedEventRevision { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

/// <summary>Captures real fenced resource ancestry; observation is not target adoption.</summary>
public sealed record RecordModerationResourceGenerationSource : ModerationQualificationMutation
{
    public required Guid SourceId { get; init; }
    public required Guid ResourceId { get; init; }
    public required long ExpectedResourceRevision { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record AdoptModerationResourceTargetSource : ModerationQualificationMutation
{
    public required Guid AdoptionId { get; init; }
    public required Guid SelectionId { get; init; }
    public required int BindingOrdinal { get; init; }
    public required Guid GenerationId { get; init; }
    public required long ExpectedGenerationRevision { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record InstallModerationConsumerSource : ModerationQualificationMutation
{
    public required Guid InstallationId { get; init; }
    public required string ConsumerId { get; init; }
    public required ModerationNativeMode Mode { get; init; }
    public required string ExpectedRuntimeGeneration { get; init; }
    public required string ExpectedInventoryFingerprint { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record WithdrawModerationConsumerSource : ModerationQualificationMutation
{
    public required Guid InstallationId { get; init; }
    public required long ExpectedInstallationRevision { get; init; }
}

public sealed record AdoptModerationProfileSource : ModerationQualificationMutation
{
    public required Guid AdoptionId { get; init; }
    public required Guid SelectionId { get; init; }
    public required ModerationNativeMode Mode { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record RevokeModerationProfileSource : ModerationQualificationMutation
{
    public required Guid AdoptionId { get; init; }
    public required long ExpectedAdoptionRevision { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
}

public sealed record AdoptModerationEventTargetSource : ModerationQualificationMutation
{
    public required Guid AdoptionId { get; init; }
    public required Guid SelectionId { get; init; }
    public required int BindingOrdinal { get; init; }
    public required Guid GenerationId { get; init; }
    public required long ExpectedGenerationRevision { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record RevokeModerationEventTargetSource : ModerationQualificationMutation
{
    public required Guid AdoptionId { get; init; }
    public required long ExpectedAdoptionRevision { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
}

public sealed record RevokeModerationResourceTargetSource : ModerationQualificationMutation
{
    public required Guid AdoptionId { get; init; }
    public required long ExpectedAdoptionRevision { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
}

/// <summary>Persists a complete draft selection; native target approval is a separate operation.</summary>
public sealed record RecordModerationContributionSelectionSource : ModerationQualificationMutation
{
    public required Guid SelectionId { get; init; }
    public required Guid EffectId { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required Guid DecisionId { get; init; }
    public required long ExpectedDecisionRevision { get; init; }
    public required Guid PolicyId { get; init; }
    public required long ExpectedPolicyRevision { get; init; }
    public required Guid PublicationId { get; init; }
    public required Guid ProfileId { get; init; }
    public required Explore.Application.Features.Moderation.Policy.ModerationPolicyContribution Contribution { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}
