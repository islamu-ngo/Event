using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public sealed record ModerationContentResponseRequest(
    Guid TenantId, Guid NoticeId, Guid OperationId, string SnapshotHash,
    Guid AssignmentId, long AssignmentRevision, string Statement)
{
    public override string ToString() => nameof(ModerationContentResponseRequest);
}

/// <summary>
/// Current protected response capability for one original occurrence, not a historical permission.
/// RemedyUntil comes only from an actual acknowledgement; a null value never prevents an earlier
/// response. Availability and submissions cannot acknowledge notification or complete a recipe.
/// </summary>
public sealed record ModerationContentResponseAvailability(
    Guid NoticeId, Guid EffectId, Guid CaseId, Guid ContentSubjectId,
    Guid AssignmentId, long AssignmentRevision, string SnapshotHash,
    DateTimeOffset RetainUntil, int ContestationCalendarMonths,
    DateTimeOffset? NotifiedAt, DateTimeOffset? RemedyUntil, DateTimeOffset QualifiedUntil)
{
    public override string ToString() => nameof(ModerationContentResponseAvailability);
}

public sealed record ModerationContentResponseWriteResult(
    ModerationQualificationWriteOutcome Outcome, ModerationContentResponse? Response = null);

/// <summary>Qualified review metadata, without statement bytes or representative identity.</summary>
public sealed record ModerationContentResponseSummary(
    Guid Id, Guid CaseId, Guid EffectId, DateTimeOffset SubmittedAt,
    DateTimeOffset RetainUntil, DateTimeOffset QualifiedUntil);

public interface IModerationContentResponseRepository
{
    Task<ModerationContentResponseWriteResult> SubmitContentResponseAsync(
        ModerationContentResponseRequest request, CancellationToken cancellationToken);

    /// <summary>Prepares and releases the affordance only inside retained current privacy and Event authority.</summary>
    Task<ModerationQualificationWriteOutcome> DiscloseContentResponseAvailabilityAsync(
        Guid tenantId, Guid noticeId,
        Func<ModerationContentResponseAvailability, CancellationToken, Task<ReadOnlyMemory<byte>>> prepare,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> write,
        CancellationToken cancellationToken);

    /// <summary>
    /// Releases response text only to a currently independent, qualified case reviewer.
    /// Current custody is retained through output; the caller never receives an unfenced plaintext result.
    /// </summary>
    Task<ModerationQualificationWriteOutcome> DiscloseContentResponseStatementAsync(
        Guid tenantId, Guid responseId,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> write,
        CancellationToken cancellationToken);

    /// <summary>Lists one bounded original-response page only within current independent reviewer custody.</summary>
    Task<ModerationQualificationWriteOutcome> DiscloseContentResponseIndexAsync(
        Guid tenantId, Guid caseId, Guid? afterId, int limit,
        Func<IReadOnlyList<ModerationContentResponseSummary>, CancellationToken, Task<ReadOnlyMemory<byte>>> prepare,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> write,
        CancellationToken cancellationToken);
}
