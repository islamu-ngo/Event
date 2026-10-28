using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Contracts.Moderation;

/// <summary>Native intents only. Tenant and authenticated actor are resolved by the repository.</summary>
public abstract record ModerationPreFindingOperation(Guid OperationId, Guid CaseId, long ExpectedCaseRevision)
{
    public sealed override string ToString() => nameof(ModerationPreFindingOperation);
}

public sealed record ProposeModerationPreFindingCustody(
    Guid OperationId, Guid CaseId, long ExpectedCaseRevision, Guid PrivacyOwnerHumanId,
    ModerationPreFindingCustodyTerms Terms, DateTimeOffset ValidUntil)
    : ModerationPreFindingOperation(OperationId, CaseId, ExpectedCaseRevision);

public sealed record AcceptModerationPreFindingCustody(
    Guid OperationId, Guid CaseId, long ExpectedCaseRevision, Guid CustodyId, long ExpectedCustodyRevision)
    : ModerationPreFindingOperation(OperationId, CaseId, ExpectedCaseRevision);

public sealed record IssueModerationPreFindingNotice(
    Guid OperationId, Guid CaseId, long ExpectedCaseRevision, Guid CustodyId,
    ModerationNoticeContentRepresentative Recipient, ModerationPreFindingText Text, DateTimeOffset RetainUntil)
    : ModerationPreFindingOperation(OperationId, CaseId, ExpectedCaseRevision);

public sealed record AcknowledgeModerationPreFindingNotice(
    Guid OperationId, Guid CaseId, long ExpectedCaseRevision, Guid NoticeId, long ExpectedNoticeRevision)
    : ModerationPreFindingOperation(OperationId, CaseId, ExpectedCaseRevision);

public sealed record RespondToModerationPreFindingNotice(
    Guid OperationId, Guid CaseId, long ExpectedCaseRevision, Guid NoticeId, long ExpectedNoticeRevision, string Statement)
    : ModerationPreFindingOperation(OperationId, CaseId, ExpectedCaseRevision);

public sealed record WithdrawModerationPreFindingCustody(
    Guid OperationId, Guid CaseId, long ExpectedCaseRevision, Guid CustodyId, long ExpectedCustodyRevision)
    : ModerationPreFindingOperation(OperationId, CaseId, ExpectedCaseRevision);

/// <summary>Independently authored subject-safe account; never the confidential occurrence or evidence source.</summary>
public sealed record ModerationPreFindingText(
    ModerationNoticeSafeText Allegation, ModerationNoticeSafeText ApplicableRule,
    ModerationNoticeSafeText ResponseInstructions)
{
    public override string ToString() => nameof(ModerationPreFindingText);
}

/// <summary>No occurrence, attribution, private evidence, reviewer identity or manager history is disclosed.</summary>
public sealed record ModerationPreFindingView(
    Guid NoticeId, long Revision, Guid CaseId, long CaseRevision, ModerationPreFindingText Notice,
    DateTimeOffset RetainUntil, DateTimeOffset? AcknowledgedAt, DateTimeOffset? RespondedAt,
    string? Response)
{
    public override string ToString() => nameof(ModerationPreFindingView);
}

public sealed record ModerationPreFindingWriteResult(
    ModerationQualificationWriteOutcome Outcome, Guid? Id = null, long? Revision = null);

public interface IModerationPreFindingRepository
{
    Task<ModerationPreFindingWriteResult> ExecutePreFindingAsync(
        ModerationPreFindingOperation operation, CancellationToken cancellationToken);

    /// <summary>
    /// Private bytes are prepared, requalified and written within one retained native custody.
    /// A GET does not acknowledge. The host must send private/no-store and never serialize an entity.
    /// </summary>
    Task<ModerationQualificationWriteOutcome> DisclosePreFindingAsync(
        Guid tenantId, Guid noticeId, bool reviewResponse,
        Func<ModerationPreFindingView, CancellationToken, Task<ReadOnlyMemory<byte>>> prepare,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> write, CancellationToken cancellationToken);

    /// <summary>Real physical payload erasure; expired metadata cannot qualify a finding.</summary>
    Task<int> PurgeExpiredPreFindingAsync(int batchSize, CancellationToken cancellationToken);
}

/// <summary>
/// Internal native finding qualification, not an evidence-disclosure API. Discovery must occur
/// before the parent acquires its human/host/epoch fences. Acquire may run at a dirty pre-save
/// checkpoint; Recheck requires a clean transaction at the actual terminal commit checkpoint.
/// The parent retains selected privacy identity custody through those checkpoints and commit.
/// </summary>
public interface IModerationPreFindingResponseOwner
{
    Task<IReadOnlyList<Guid>?> DiscoverResponseHumansAsync(Guid caseId, CancellationToken cancellationToken);
    Task<ModerationFindingEvidenceReference?> AcquireCurrentResponseAsync(
        Guid caseId, Guid responseId, CancellationToken cancellationToken);
    Task<ModerationFindingEvidenceReference?> AcquireCurrentResponseAsync(
        IModerationFindingProvenanceSession provenance, Guid caseId, Guid responseId, CancellationToken cancellationToken);
    Task<bool> RecheckResponseAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Opaque native parent custody for finding provenance only. Implementing this interface or
/// copying coordinates supplies no authority, private disclosure or human reviewer permission.
/// </summary>
public interface IModerationFindingProvenanceSession { }
