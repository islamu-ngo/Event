using Explore.Domain;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Contracts.Moderation;

/// <summary>
/// Internal authorized review intent, not reporter input. Subject coordinates and responsibility
/// are claims to qualify against actual native ownership; no claimed authority is accepted.
/// The exact read grant selects retained material. OperationId identifies an immutable retry.
/// </summary>
public sealed record ModerationCaseAttributionRequest(
    Guid OperationId, Guid OccurrenceCaseId, long ExpectedCaseRevision, long EvidenceCaseRevision,
    Guid ReadGrantId, ModerationResponsibilityBasis Basis, ModerationSubjectKind SubjectKind,
    Guid SubjectId, DateTimeOffset ValidUntil)
{
    public Guid? InvestigationSourceId { get; init; }
    public long? InvestigationSourceRevision { get; init; }

    public override string ToString() => nameof(ModerationCaseAttributionRequest);
}

/// <summary>
/// Fresh qualification for the same open subject proceeding. The expected head tuple and
/// subject-case revision identify the transition; Qualification.OperationId identifies its exact retry.
/// </summary>
public sealed record ModerationCaseAttributionRenewalRequest(
    Guid CaseId, long ExpectedCaseRevision, Guid ExpectedSourceId,
    long ExpectedGeneration, long ExpectedHeadRevision, ModerationCaseAttributionRequest Qualification)
{
    public override string ToString() => nameof(ModerationCaseAttributionRenewalRequest);
}

/// <summary>Owns native serializable attribution/renewal/withdrawal and their final rechecks.</summary>
public interface IModerationCaseAttributionRepository
{
    Task<ModerationCase?> AttributeAsync(ModerationCaseAttributionRequest request, CancellationToken cancellationToken);
    Task<ModerationCaseAttributionSource?> RenewAsync(
        ModerationCaseAttributionRenewalRequest request, CancellationToken cancellationToken);
    Task<ModerationCaseAttributionSource?> WithdrawAsync(
        Guid sourceId, Guid operationId, long expectedRevision, CancellationToken cancellationToken);
}

/// <summary>
/// The enrolled native target owner stages only. SaveChanges followed by RecheckAttributionAsync
/// immediately before commit is mandatory. AcquireCurrentAttributionAsync is an internal guard,
/// not disclosure permission; its final recheck is also mandatory in the consuming transaction.
/// Returning a linked case never authorizes serialization of confidential occurrence coordinates.
/// </summary>
public interface IModerationCaseAttributionOwner
{
    Task<ModerationCase?> StageAttributionAsync(
        ModerationCaseAttributionRequest request, Guid caseId, CancellationToken cancellationToken);
    Task<ModerationCaseAttributionSource?> StageAttributionRenewalAsync(
        ModerationCaseAttributionRenewalRequest request, CancellationToken cancellationToken);
    Task<ModerationCaseAttributionSource?> StageAttributionWithdrawalAsync(
        Guid sourceId, Guid operationId, long expectedRevision, CancellationToken cancellationToken);
    Task<ModerationCaseAttributionSource?> AcquireCurrentAttributionAsync(Guid caseId, CancellationToken cancellationToken);
    Task<ModerationCaseAttributionSource?> AcquireCurrentAttributionAsync(
        IModerationFindingProvenanceSession provenance, Guid caseId, CancellationToken cancellationToken);
    Task<bool> RecheckAttributionAsync(CancellationToken cancellationToken);
}
