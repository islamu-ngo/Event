using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Responses;

namespace Explore.Application.Features.Moderation.Requests.Commands;

public sealed record RecordModerationResourceSafetyReviewCommand(ModerationResourceSafetyReviewIntent Intent)
    : ICommand<ModerationResourceSafetyResponse>;
public sealed record ReleaseModerationResourceQuarantineCommand(ModerationResourceQuarantineReleaseIntent Intent)
    : ICommand<ModerationResourceSafetyResponse>;

public sealed record ModerationResourceSafetyResponse : BaseCommandResponse<Guid>
{
    private ModerationResourceSafetyResponse(BaseCommandResponse<Guid> state, DateTimeOffset? recordedAt = null)
        : base(state, true) => RecordedAt = recordedAt;

    public DateTimeOffset? RecordedAt { get; }

    internal static ModerationResourceSafetyResponse From(
        ModerationResourceSafetyOutcome outcome, Guid operationId, Guid? receiptId, DateTimeOffset? recordedAt) =>
        outcome switch
        {
            ModerationResourceSafetyOutcome.Known when receiptId is { } id =>
                new(BaseCommandResponse.Success(id), recordedAt),
            ModerationResourceSafetyOutcome.Conflict => new(BaseCommandResponse.Conflict(operationId)),
            ModerationResourceSafetyOutcome.Denied => new(BaseCommandResponse.Authorization<Guid>()),
            _ => new(BaseCommandResponse.Failure<Guid>("ModerationAuthorityUnavailable"))
        };

    internal static ModerationResourceSafetyResponse Invalid(IEnumerable<string> errors) =>
        new(BaseCommandResponse.Validation<Guid>(errors));
}
