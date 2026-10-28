using System.Text.Json.Serialization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;
using Explore.Domain;

namespace Explore.Application.Features.Moderation.RecognitionRecovery;

public sealed record GetRecognitionRecoveryEscalationsQuery(Guid? AfterId, int Limit)
    : IQuery<RecognitionRecoveryReadResponse<IReadOnlyList<RecognitionRecoveryEscalationDto>>>;

public sealed record GetRecognitionRecoveryEscalationQuery(Guid Id)
    : IQuery<RecognitionRecoveryReadResponse<RecognitionRecoveryEscalationDto>>;

public sealed record GetRecognitionRecoveryContextQuery(Guid EscalationId)
    : IQuery<RecognitionRecoveryReadResponse<RecognitionRecoveryContextDto>>;

/// <summary>The receipt is supplied only by a purpose-authenticated adapter, never by a JSON body.</summary>
public sealed record GetRecognitionCorrectionProofsQuery : IQuery<RecognitionRecoveryReadResponse<IReadOnlyList<RecognitionCorrectionProofDto>>>
{
    public GetRecognitionCorrectionProofsQuery(Guid authenticatedIntentId, string receipt)
    {
        AuthenticatedIntentId = authenticatedIntentId;
        Receipt = receipt;
    }
    public Guid AuthenticatedIntentId { get; }
    [JsonIgnore] public string Receipt { get; }
    public override string ToString() => nameof(GetRecognitionCorrectionProofsQuery);
}

public sealed record RecognitionRecoveryEscalationDto(
    Guid Id, Guid IntentId, Guid SourceId, Guid? ActionRecoveryId, long CapturedPendingRevision,
    ModerationRecognitionEscalationStage Stage, DateTimeOffset RequiredAt, DateTimeOffset OriginalDeadline,
    DateTimeOffset? DeliveredAt, DateTimeOffset? AcknowledgedAt)
{
    [JsonIgnore] public Guid QualifiedOwnerHumanId { get; init; }
}

public sealed record RecognitionRecoveryContextDto(
    Guid EscalationId, Guid IntentId, long PendingRevision,
    ModerationRecognitionPendingState State, ModerationRecognitionPendingFailure Failure,
    DateTimeOffset ReceivedAt, DateTimeOffset SettleBy, DateTimeOffset EscalateAt,
    int RequiredSourceCount, Guid SourceId, long SourceRevision, long SourceGeneration,
    Guid AdoptionId, long AdoptionRevision, long AdoptionGeneration, DateTimeOffset MaximumValidUntil,
    ModerationRecognitionDispositionGrounds? AvailableGrounds,
    Guid? DispositionId, long? DispositionRevision, ModerationRecognitionDispositionGrounds? AssessedGrounds,
    DateTimeOffset? AssessedAt, DateTimeOffset? AssessmentValidUntil, DateTimeOffset? AuthorizedAt)
{
    [JsonIgnore] public Guid QualifiedOwnerHumanId { get; init; }
    [JsonIgnore] public bool CanAssess { get; init; }
    [JsonIgnore] public bool CanAuthorize { get; init; }
}

/// <summary>Correction evidence exposes factual authorization dates and grounds, not stored proof digests.</summary>
public sealed record RecognitionCorrectionProofDto(Guid Id, Guid IntentId, Guid SourceId, long PendingRevision,
    ModerationRecognitionDispositionGrounds Grounds, DateTimeOffset ProtectedAt, DateTimeOffset RemedyUntil,
    DateTimeOffset RetainUntil);

public sealed record RecognitionRecoveryReadResponse<T> : BaseCommandResponse<Guid> where T : class
{
    private RecognitionRecoveryReadResponse(BaseCommandResponse<Guid> state, T? data) : base(state, true) => Data = data;
    public T? Data { get; }
    public static RecognitionRecoveryReadResponse<T> Found(Guid id, T data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new(BaseCommandResponse.Success(id), data);
    }
    public static RecognitionRecoveryReadResponse<T> Failed(BaseCommandResponse<Guid> failure) =>
        new(BaseCommandResponse.RequireFailure(failure), null);
}

internal static class RecognitionRecoveryMapping
{
    public const string Unavailable = "RecognitionRecoveryUnavailable";

    public static RecognitionRecoveryEscalationDto ToDto(ModerationRecognitionEscalation row) =>
        new(row.Id, row.IntentId, row.SourceId, row.ActionRecoveryId, row.PendingRevision, row.Stage,
            row.RequiredAt, row.OriginalDeadline, row.DeliveredAt, row.AcknowledgedAt)
        { QualifiedOwnerHumanId = row.RecipientHumanId };

    public static RecognitionRecoveryContextDto ToDto(ModerationRecognitionOwnerContext row) =>
        new(row.EscalationId, row.IntentId, row.PendingRevision, row.State, row.Failure,
            row.ReceivedAt, row.SettleBy, row.EscalateAt, row.RequiredSourceCount,
            row.SourceId, row.SourceRevision, row.SourceGeneration, row.AdoptionId, row.AdoptionRevision,
            row.AdoptionGeneration, row.MaximumValidUntil, row.AvailableGrounds, row.DispositionId,
            row.DispositionRevision, row.AssessedGrounds, row.AssessedAt, row.AssessmentValidUntil, row.AuthorizedAt)
        { QualifiedOwnerHumanId = row.QualifiedOwnerHumanId, CanAssess = row.CanAssess, CanAuthorize = row.CanAuthorize };
}
