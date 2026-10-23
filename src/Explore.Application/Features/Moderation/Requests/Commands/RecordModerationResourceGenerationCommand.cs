using Explore.Application.Contracts.Persistence;

namespace Explore.Application.Features.Moderation.Requests.Commands;

public sealed record RecordModerationResourceGenerationCommand : ModerationQualificationCommand
{
    public required Guid SourceId { get; init; }
    public required Guid ResourceId { get; init; }
    public required long ExpectedResourceRevision { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
    internal override ModerationQualificationMutation ToMutation() => new RecordModerationResourceGenerationSource
    {
        OperationId = OperationId, Target = Target, ExpectedQualificationRevision = ExpectedQualificationRevision,
        SourceId = SourceId, ResourceId = ResourceId, ExpectedResourceRevision = ExpectedResourceRevision,
        ValidUntil = ValidUntil
    };
}
