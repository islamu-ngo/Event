using Explore.Application.Contracts.Persistence;

namespace Explore.Application.Features.Moderation.Requests.Commands;

public sealed record RecordModerationEventGenerationCommand : ModerationQualificationCommand
{
    public required Guid SourceId { get; init; }
    public required Guid EventId { get; init; }
    public required long ExpectedEventRevision { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
    internal override ModerationQualificationMutation ToMutation() => new RecordModerationEventGenerationSource
    {
        OperationId = OperationId, Target = Target, ExpectedQualificationRevision = ExpectedQualificationRevision,
        SourceId = SourceId, EventId = EventId, ExpectedEventRevision = ExpectedEventRevision, ValidUntil = ValidUntil
    };
}
