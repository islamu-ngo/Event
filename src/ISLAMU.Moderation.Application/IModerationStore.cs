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
    Task<ModerationDecision?> FindDecisionAsync(
        Guid decisionId, SubjectReference subject, AuthorityScope scope, CancellationToken cancellationToken);

    Task<ModerationSanction?> FindSanctionAsync(
        Guid sanctionId, SubjectReference subject, AuthorityScope scope, CancellationToken cancellationToken);

    Task<IReadOnlyList<ModerationSanction>> ReadSanctionsAsync(SubjectReference subject, CancellationToken cancellationToken);
    Task<IReadOnlyList<ModerationProtectiveMeasure>> ReadProtectionAsync(SubjectReference subject, CancellationToken cancellationToken);

    /// <summary>Stages entity changes and the exact effect with the authority generations that must remain current at commit.</summary>
    Task SaveAsync(
        ModerationDecision decision, ModerationSanction? sanction, ModerationAuthorityGrant grant,
        ModerationPlannedEffect effect, CancellationToken cancellationToken);
}
