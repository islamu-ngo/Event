namespace Explore.Domain;

/// <summary>
/// A transaction-qualified view of one recipient's current source. It contains no
/// subject identity, recognition material, authentication hash or frozen proof digest.
/// The coverage count is not permission to act for any other contributing source.
/// </summary>
public sealed record ModerationRecognitionOwnerContext(
    Guid EscalationId, Guid IntentId, Guid QualifiedOwnerHumanId, long PendingRevision,
    ModerationRecognitionPendingState State, ModerationRecognitionPendingFailure Failure,
    DateTimeOffset ReceivedAt, DateTimeOffset SettleBy, DateTimeOffset EscalateAt,
    int RequiredSourceCount, Guid SourceId, long SourceRevision, long SourceGeneration,
    Guid AdoptionId, long AdoptionRevision, long AdoptionGeneration, DateTimeOffset MaximumValidUntil,
    ModerationRecognitionDispositionGrounds? AvailableGrounds,
    Guid? DispositionId, long? DispositionRevision, ModerationRecognitionDispositionGrounds? AssessedGrounds,
    DateTimeOffset? AssessedAt, DateTimeOffset? AssessmentValidUntil, DateTimeOffset? AuthorizedAt,
    bool CanAssess, bool CanAuthorize);
