using System.Text.Json.Serialization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;
using Explore.Domain;

namespace Explore.Application.Features.Moderation.RecognitionRecovery.Commands;

public sealed record AssessRecognitionRecoveryCommand : ICommand<BaseCommandResponse<Guid>>
{
    [JsonIgnore] public Guid IntentId { get; init; }
    [JsonIgnore] public Guid SourceId { get; init; }
    public required Guid OperationId { get; init; }
    public required long ExpectedPendingRevision { get; init; }
    public required long ExpectedSourceRevision { get; init; }
    public required long ExpectedSourceGeneration { get; init; }
    public required Guid AdoptionId { get; init; }
    public required long ExpectedAdoptionRevision { get; init; }
    public required long ExpectedAdoptionGeneration { get; init; }
    public required ModerationRecognitionDispositionGrounds Grounds { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record AuthorizeRecognitionRecoveryCommand : ICommand<BaseCommandResponse<Guid>>
{
    [JsonIgnore] public Guid Id { get; init; }
    public required long ExpectedRevision { get; init; }
}

public sealed record AcknowledgeRecognitionRecoveryCommand(Guid Id) : ICommand<BaseCommandResponse<Guid>>;
