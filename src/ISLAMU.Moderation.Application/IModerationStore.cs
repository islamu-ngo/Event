using ISLAMU.Moderation.Domain;

namespace ISLAMU.Moderation.Application;

/// <summary>Owns the complete local retry unit, exact request-bound receipt and current qualification/subject/policy fences.</summary>
/// <remarks>
/// A mutation commits only a Known result, its entities and its effect together. Every other outcome rolls back.
/// The host must reject operation-ID reuse with a different canonical request, verify exact ambiguous commits,
/// and hold writer fences or atomically validate authority generations through commit. No external effect runs here.
/// </remarks>
public interface IModerationStore
{
    Task<ModerationOperationResult> ExecuteAsync(
        ModerationOperation operation,
        Func<IModerationSession, CancellationToken, Task<ModerationOperationResult>> execute,
        CancellationToken cancellationToken);
}

/// <summary>Entity access within the store-owned authority fence; never an unfenced or lasting authorization permit.</summary>
public interface IModerationSession
{
    /// <summary>
    /// Adjudication access requires the complete subject and requested action scope. An
    /// attributed tenant-owned case may contain a separately qualified resource action;
    /// its custody scope is not permission for that action. Target-only allegations are excluded.
    /// </summary>
    Task<ModerationCase?> FindCaseAsync(
        Guid caseId, SubjectReference subject, AuthorityScope actionScope, CancellationToken cancellationToken);

    Task<ModerationDecision?> FindDecisionAsync(
        Guid decisionId, SubjectReference subject, AuthorityScope scope, CancellationToken cancellationToken);

    Task<ModerationFinding?> FindFindingAsync(
        Guid findingId, SubjectReference subject, AuthorityScope scope, CancellationToken cancellationToken);

    Task<ModerationSanction?> FindSanctionAsync(
        Guid sanctionId, SubjectReference subject, AuthorityScope scope, CancellationToken cancellationToken);

    Task<IReadOnlyList<ModerationSanction>> ReadSanctionsAsync(SubjectReference subject, CancellationToken cancellationToken);
    Task<IReadOnlyList<ModerationProtectiveMeasure>> ReadProtectionAsync(SubjectReference subject, CancellationToken cancellationToken);

    /// <summary>Stages the decision, optional contribution and optional specifically superseded contribution as one atomic unit.</summary>
    /// <remarks>
    /// The host fences the exact case/decision/contribution reads, checks insertion identity uniqueness, and persists only
    /// changed entities with their original revisions. The exact effect and authority generations remain current through commit.
    /// Canonical receipts bind every proposal term and both contribution coordinates and revisions, not only the operation ID.
    /// </remarks>
    Task SaveAsync(
        ModerationDecision decision, ModerationSanction? sanction, ModerationSanction? supersededSanction,
        ModerationAuthorityGrant grant,
        ModerationPlannedEffect effect, CancellationToken cancellationToken);
}
