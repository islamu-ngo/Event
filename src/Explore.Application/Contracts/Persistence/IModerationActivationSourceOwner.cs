using System.Collections.Immutable;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Native deployment, profile and measured-conformance owners join the caller's primary transaction.
/// This port is not implemented by policy definitions, effect receipts, configuration booleans or
/// the capability registry. Composition must supply actual owners; there is no permissive default.
/// </summary>
public interface IModerationActivationSourceOwner
{
    /// <summary>
    /// Acquires the actual deployment/profile writer fences through the original borrowed acquisition
    /// and reads the current adopted profile for this exact publication. The scoped implementation must use the same primary context and
    /// transaction, retain every fence until its completion, and return null for missing authority.
    /// Mode, required prerequisites and the finite validity deadline come from the native
    /// deployment/profile, never the request. Its expiry bounds the entire activation qualification.
    /// </summary>
    Task<ModerationActivationProfileObservation?> AcquireProfileAsync(
        IModerationConsumerEvidenceSession evidenceSession,
        Guid hostBindingId, Guid transactionId, ModerationPolicyCompiledDraft policy,
        ModerationPolicyCompiledProfile profile, ModerationDecision decision,
        DateTimeOffset observedAt, CancellationToken cancellationToken);

    /// <summary>
    /// Reads and requalifies the measured conformance receipt under its native writer fence.
    /// Passed requires completed measurements for the exact inventory contract and native generation.
    /// Authored/unrun tests and inventory support labels cannot produce a registration.
    /// </summary>
    Task<ModerationCapabilityRegistration?> ReadConformanceAsync(
        IModerationConsumerEvidenceSession evidenceSession,
        Guid transactionId, ModerationActivationBinding binding, string consumerId,
        DateTimeOffset observedAt, CancellationToken cancellationToken);

    /// <summary>
    /// Independently acquires and reads the current native consumer source. Do not derive these
    /// coordinates from the conformance receipt; both observations must remain fenced through commit.
    /// </summary>
    Task<ModerationCapabilitySourceObservation?> AcquireConsumerAsync(
        IModerationConsumerEvidenceSession evidenceSession,
        Guid transactionId, ModerationActivationBinding binding, string consumerId,
        DateTimeOffset observedAt, CancellationToken cancellationToken);

    /// <summary>
    /// Acquires the named prerequisite owner's current decision and its independently current source
    /// through the supplied original acquisition, never a transaction-ID-only replacement.
    /// The owner checks its substantive authority, adoption, expiry, withdrawal and native generation.
    /// Missing owners/evidence return null rather than an invented approval.
    /// </summary>
    Task<ModerationPrerequisiteObservation?> AcquirePrerequisiteAsync(
        IModerationConsumerEvidenceSession evidenceSession,
        Guid transactionId, ModerationActivationBinding binding, ModerationActivationPrerequisite prerequisite,
        DateTimeOffset observedAt, CancellationToken cancellationToken);
}

public enum ModerationActivationPrerequisite
{
    ConstitutionAndReviewers = 1,
    RecognitionAndPrivacy = 2,
    EventRightsAndPremises = 3,
    ChildAndAgePolicy = 4,
    MeasuredOperations = 5
}

/// <summary>Current native profile authority, separately qualified from Domain rule definitions.</summary>
public sealed record ModerationActivationProfileObservation(
    Guid TransactionId, Guid HostBindingId, Guid PolicyId, long PolicyRevision, Guid PublicationId,
    AuthorityScope Jurisdiction, Guid ProfileId, Guid DecisionId, long DecisionRevision,
    ModerationRuleReference Rule, ModerationNativeMode Mode,
    Guid SourceId, long SourceRevision, string WriterGeneration, DateTimeOffset ObservedAt, DateTimeOffset ValidUntil,
    ImmutableArray<ModerationActivationPrerequisite> RequiredPrerequisites,
    ImmutableArray<ModerationCapability> Capabilities, ModerationPolicyContribution Contribution);

public sealed record ModerationQualificationPolicyCurrentSource(
    Guid TransactionId, Guid HostBindingId, Guid CaseId, AuthorityScope Scope,
    Guid SourceId, long SourceRevision, string WriterGeneration,
    TimeSpan MaximumReviewerLifetime, TimeSpan MaximumUrgencyLifetime,
    DateTimeOffset ObservedAt, DateTimeOffset ValidUntil);

public sealed record ModerationQualificationPolicyApproval(
    Guid TransactionId, Guid HostBindingId, Guid CaseId, AuthorityScope Scope,
    Guid ApprovalId, long ApprovalRevision, Guid ApprovedSourceId, long ApprovedSourceRevision,
    string ApprovedWriterGeneration, TimeSpan MaximumReviewerLifetime, TimeSpan MaximumUrgencyLifetime,
    DateTimeOffset AdoptedAt, DateTimeOffset ValidUntil, DateTimeOffset ObservedAt);

/// <summary>Matched observations, not a permission grant; the native caller must retain both owner fences.</summary>
public sealed record ModerationQualificationPolicyObservation(
    ModerationQualificationPolicyCurrentSource Current, ModerationQualificationPolicyApproval Approval)
{
    public DateTimeOffset ValidUntil => Current.ValidUntil < Approval.ValidUntil
        ? Current.ValidUntil : Approval.ValidUntil;
}

/// <summary>
/// A prerequisite owner's substantive approval and independent current-source observation.
/// These are source coordinates, not an injectable "is approved" flag.
/// </summary>
public sealed record ModerationPrerequisiteObservation(
    Guid TransactionId, ModerationActivationBinding Binding, ModerationActivationPrerequisite Prerequisite,
    Guid ApprovalId, long ApprovalRevision, Guid SourceId, long ApprovedSourceRevision,
    string ApprovedWriterGeneration, long CurrentSourceRevision, string CurrentWriterGeneration,
    DateTimeOffset AdoptedAt, DateTimeOffset ValidUntil, DateTimeOffset ObservedAt);
