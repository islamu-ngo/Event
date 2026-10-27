using System.Collections.Immutable;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Moderation;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IModerationPolicyPublicationRepository
{
    /// <summary>
    /// Acquires installed native administration and latest adopted entities on an owned clean
    /// serializable transaction. Request identity/tenant are captured by the server, never supplied
    /// by the HTTP query. The lease must survive private representation and actual terminal output.
    /// An absent slot is not an absent installation and creates no reservation or epoch advance.
    /// </summary>
    Task<ModerationCurrentPolicyReadAcquisition> AcquireCurrentAuthoringAsync(
        ModerationCurrentPolicyReadRequest request, CancellationToken cancellationToken = default);

    /// <summary>Resolves one exact active native tenant and independently acquires root mutation authority.</summary>
    Task<Tenant> AcquireInstanceConfigurationImportTargetAsync(
        string slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the complete original reservation before any write and tracks only exact native
    /// advances on this transaction. The returned completion must be verified after all import
    /// sections and receipt writes, immediately before the transaction owner commits.
    /// </summary>
    Task<IModerationConfigurationImportCompletion> ApplyConfigurationImportSetInCurrentTransactionAsync(
        ConfigurationImportTarget target, IReadOnlyList<ModerationConfigurationImportSource> sources,
        ModerationConfigurationImportReservationSet reservation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads latest adopted native definitions under the caller's original serializable transaction.
    /// Instance export requires fenced native platform administration and includes active tenants;
    /// tenant export is restricted to the actual request tenant. No context impersonation,
    /// portable mapping, deployment readiness or activation authority is supplied by this read.
    /// </summary>
    Task<IReadOnlyList<ModerationPolicyPublication>> AcquireConfigurationExportAsync(
        ConfigurationImportTarget target, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads independently authorized native destination entities under the caller's serializable
    /// transaction. No supplied compilation context, mapping or baseline document grants authority.
    /// The retained baseline must belong to the selected policy's adopted predecessor chain.
    /// </summary>
    Task<ModerationConfigurationImportDestination> AcquireConfigurationImportDestinationAsync(
        ConfigurationImportTarget target, Guid? baselinePublicationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires a tenant destination through explicit native instance-administration mutation
    /// authority, not the request-tenant destination contract or export permission. Requires the
    /// original serializable transaction, a live human platform.admin, active request and target
    /// tenants, and their installed epochs. Returns target-owned entities and adopted history;
    /// it neither changes request/provider tenant state nor installs missing configuration.
    /// Every rejection rolls back the original transaction. Fences survive until its completion.
    /// </summary>
    Task<ModerationConfigurationImportDestination> AcquireInstanceConfigurationImportDestinationAsync(
        Guid targetTenantId, Guid? baselinePublicationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reacquires native instance administration and the exact target's current entities, then
    /// recompiles complete preview-bound meaning into an immutable adopted Draft. Requires a
    /// clean tracker after preceding sections flush, advances only the target epoch and latest
    /// pointer, and never commits or moves the active pointer. Failure rolls back all earlier
    /// sections and target drafts in the original transaction; the caller must abandon its tracker.
    /// Each destination needs its own operation identity. A preview grants no mutation authority.
    /// </summary>
    Task<ModerationPolicyPublication> ApplyInstanceConfigurationImportInCurrentTransactionAsync(
        Guid operationId, Guid targetTenantId, ModerationConfigurationDocument incoming,
        ModerationConfigurationImportPreview preview, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reacquires authority, history and mappings and recompiles the preview-bound complete draft.
    /// Requires a clean tracker, flushes only this participant's graph, and never commits.
    /// Any rejection rolls back the original transaction, including earlier import sections.
    /// The coordinator must abandon the failed unit of work, not continue using its tracker.
    /// </summary>
    Task<ModerationPolicyPublication> ApplyConfigurationImportInCurrentTransactionAsync(
        Guid operationId, ConfigurationImportTarget target, ModerationConfigurationDocument incoming,
        ModerationConfigurationImportPreview preview, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores an approved server reservation using fresh native entities in the original transaction.
    /// Only exact legal aggregates returned by preceding import sections may be flushed first.
    /// A failure invalidates the entire transaction, including those preceding sections.
    /// </summary>
    Task<ModerationPolicyPublication> ApplyConfigurationImportReservationInCurrentTransactionAsync(
        Guid operationId, ConfigurationImportTarget target, ModerationConfigurationDocument incoming,
        ModerationConfigurationImportReservation reservation, IReadOnlyList<LegalDocument> ownedLegalDocuments,
        CancellationToken cancellationToken = default);

    /// <summary>Commits immutable future publication content and identity, without moving either selection pointer.</summary>
    Task<ModerationPolicyPublicationCandidateResult> PrepareAsync(
        ModerationPolicyPublicationRequest request, DateTimeOffset validUntil,
        CancellationToken cancellationToken = default);

    /// <summary>Retains the exact candidate and predecessor on the caller's original serializable transaction.</summary>
    Task<ModerationPolicyPublicationCandidate?> AcquireCandidateAsync(
        Guid transactionId, Guid hostBindingId, AuthorityScope jurisdiction,
        Guid policyId, Guid publicationId, DateTimeOffset observedAt,
        CancellationToken cancellationToken = default);

    Task<ModerationPolicyPublicationResult> PublishAsync(
        ModerationPolicyPublicationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Joins the caller's serializable primary transaction and retains the jurisdiction slot fence
    /// until commit. Returns the complete persisted graph, not permission or a conformance receipt.
    /// activeOnly=false reads latest adopted authoring state; true reads the independently active pointer.
    /// </summary>
    Task<ModerationPolicyPublication?> AcquireSelectedAsync(
        Guid transactionId, Guid hostBindingId, AuthorityScope jurisdiction, Guid policyId,
        bool activeOnly, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reacquires the active pointer and independently current complete deployment evidence.
    /// This is only the policy prerequisite for activation; case, human quorum, subject, ordinary
    /// authority and each actual target transition remain the operation owner's responsibility.
    /// Returns the independently current evidence deadline, or null. The caller must retain that
    /// deadline through commit; a successful acquisition is not an indefinitely valid Boolean.
    /// </summary>
    Task<DateTimeOffset?> RequalifyActivationAsync(
        IModerationConsumerEvidenceSession evidenceSession,
        Guid transactionId, Guid hostBindingId, AuthorityScope jurisdiction, Guid policyId,
        Guid publicationId, DateTimeOffset observedAt, CancellationToken cancellationToken = default);
}

public enum ModerationCurrentPolicyReadOutcome
{
    Allowed = 1, Invalid = 2, Unauthenticated = 3, Denied = 4, Unavailable = 5
}

/// <summary>Server-captured request frame; only Scope is authorable client intent.</summary>
public sealed record ModerationCurrentPolicyReadRequest(
    ModerationScopeKind Scope, Guid RequestTenantId, Guid? HumanId);

public sealed record ModerationCurrentPolicyReadAcquisition(
    ModerationCurrentPolicyReadOutcome Outcome, IModerationCurrentPolicyReadLease? Lease = null);

/// <summary>
/// Native entities and installed inventory, not a transport view or a lasting mutation grant.
/// Revalidation retains the original coordinates, including continued absence of the selected slot.
/// Disposal rolls back the owned read transaction and restores its original request frame.
/// </summary>
public interface IModerationCurrentPolicyReadLease : IAsyncDisposable
{
    ModerationHostBinding Host { get; }
    ModerationQualificationEpoch Epoch { get; }
    AuthorityScope Jurisdiction { get; }
    ModerationPolicySelection? Selection { get; }
    ModerationPolicyPublication? Publication { get; }
    ModerationCapabilityRegistry Inventory { get; }
    Task<ModerationCurrentPolicyReadOutcome> RevalidateAsync(CancellationToken cancellationToken);
}

public interface IModerationConfigurationImportCompletion
{
    Task VerifyBeforeCommitAsync(CancellationToken cancellationToken);
}

public sealed record ModerationPolicyPublicationRequest(
    Guid OperationId, Guid ExpectedHostBindingId, Guid ExpectedAuthoritySourceId,
    long ExpectedQualificationRevision, string ExpectedWriterGeneration,
    Guid? ExpectedPublicationId, ModerationPolicyProposedState Proposed)
{
    public override string ToString() => nameof(ModerationPolicyPublicationRequest);
}

public enum ModerationPolicyPublicationOutcome
{
    Known = 1, Invalid = 2, Unauthenticated = 3, Denied = 4, Conflict = 5, Unavailable = 6
}

public sealed record ModerationPolicyPublicationResult(
    ModerationPolicyPublicationOutcome Outcome, ModerationPolicyPublication? Publication = null);

public sealed record ModerationPolicyPublicationCandidateResult(
    ModerationPolicyPublicationOutcome Outcome, ModerationPolicyPublicationCandidate? Candidate = null);

public interface IModerationPolicyPublicationSelectionSource
{
    /// <summary>
    /// Selects the exact current latest or active publication for the rule's native jurisdiction.
    /// Keeps the same primary transaction's slot fence through commit. Selection is not activation.
    /// Latest declarations retain unsupported profiles; active selection additionally requires support.
    /// </summary>
    Task<ModerationPolicyPublicationSelection?> AcquireAsync(
        Guid transactionId, Guid hostBindingId, AuthorityScope target, ModerationRuleReference rule,
        long expectedPolicyRevision, bool activeOnly, CancellationToken cancellationToken);
}

public sealed record ModerationPolicyPublicationSelection(
    ModerationPolicyPublication Publication, ModerationPolicyCompiledDraft Draft,
    ModerationPolicyCompiledProfile Profile, ModerationNoticeSourceStamp Source);

/// <summary>
/// One finite complete native obligation, generated only by the publication writer. The exact
/// deployment, prerequisite, effect or all-thirteen-dimensions plan is carried, not a subset mask.
/// </summary>
public sealed record ModerationPolicyPublicationObligation(
    ModerationPolicyPublicationRequirement Publication, ModerationNativeMode Mode,
    ModerationPolicyBounds Bounds, ModerationPolicyProposedState Proposed,
    ModerationPolicyPrerequisiteRequirement? Prerequisite = null,
    ModerationPolicyEffectRequirement? Effect = null, ModerationPolicyConsequenceRequirement? Consequence = null);

public sealed record ModerationPolicyPublicationApproval(
    Guid TransactionId, string RequirementDigest, Guid ApprovalId, long ApprovalRevision,
    Guid ApprovedSourceId, long ApprovedSourceRevision, string ApprovedWriterGeneration,
    DateTimeOffset QualifiedAt, DateTimeOffset ValidUntil);

public sealed record ModerationPolicyPublicationCurrentSource(
    Guid TransactionId, string RequirementDigest, Guid SourceId, long Revision,
    string WriterGeneration, DateTimeOffset ObservedAt);

/// <summary>
/// A single native composition owner, never an enumerable plugin registry. Implementations must
/// use the supplied original evidence session, join its primary transaction and keep every actual
/// writer fence until its completion. A transaction identity alone does not establish the acquisition.
/// ReadApprovalAsync resolves substantive deployed approval, including EVERY named prerequisite
/// and all thirteen consequence modes. Effect/consequence approvals require measured exact
/// semantic target-resolver coverage, including effects without a capability. AcquireSourceAsync
/// independently reads current owning state; copying approved coordinates is not implementation.
/// No production implementation is supplied until these deployment/measurement owners exist.
/// </summary>
public interface IModerationPolicyPublicationEvidenceOwner
{
    Task<ModerationPolicyPublicationApproval?> ReadApprovalAsync(
        IModerationConsumerEvidenceSession evidenceSession,
        Guid transactionId, ModerationPolicyPublicationObligation obligation,
        DateTimeOffset observedAt, CancellationToken cancellationToken);
    Task<ModerationPolicyPublicationCurrentSource?> AcquireSourceAsync(
        IModerationConsumerEvidenceSession evidenceSession,
        Guid transactionId, ModerationPolicyPublicationObligation obligation,
        DateTimeOffset observedAt, CancellationToken cancellationToken);
    Task<ModerationCapabilityRegistration?> ReadConformanceAsync(
        IModerationConsumerEvidenceSession evidenceSession,
        Guid transactionId, ModerationActivationBinding binding, string consumerId,
        DateTimeOffset observedAt, CancellationToken cancellationToken);
    Task<ModerationCapabilitySourceObservation?> AcquireConsumerAsync(
        IModerationConsumerEvidenceSession evidenceSession,
        Guid transactionId, ModerationActivationBinding binding, string consumerId,
        DateTimeOffset observedAt, CancellationToken cancellationToken);
}

/// <summary>
/// The remaining safe-disclosure/target authority. This is not the profile owner: implementations
/// receive the actual persisted adopted profile and must separately acquire the current effect-
/// specific disclosure approval and independently qualified full target selection under its fences.
/// Finding justification is never subject disclosure. No permissive or unavailable stub is registered.
/// </summary>
public interface IModerationPolicyPublicationNoticeAuthority
{
    Task<ModerationPolicyPublicationNoticeApproval?> AcquireAsync(
        Guid transactionId, ModerationPolicyPublication publication,
        ModerationEffectIntent effect, ModerationDecision decision, ModerationSanction? sanction,
        ModerationAuthorityGrant grant, DateTimeOffset observedAt, CancellationToken cancellationToken);
}

public sealed record ModerationPolicyPublicationNoticeApproval(
    Guid TransactionId, DateTimeOffset ObservedAt, DateTimeOffset ValidUntil,
    ModerationNoticeContentApproval Approval, ModerationPolicyContribution Contribution,
    ImmutableArray<ModerationNoticeTargetObservation> Targets,
    ModerationNoticeReviewerQualification ReviewerQualification);
