using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Responses;

namespace Explore.Application.Features.Moderation.Requests.Commands;

public sealed record ApplyModerationQualificationCommand(ModerationQualificationMutation Mutation)
    : ICommand<ModerationQualificationResponse>
{
    public override string ToString() => nameof(ApplyModerationQualificationCommand);
}

public sealed record ModerationQualificationResponse : BaseCommandResponse<Guid>
{
    private ModerationQualificationResponse(
        BaseCommandResponse<Guid> state, long? revision = null, long? qualificationRevision = null)
        : base(state, true)
    {
        Revision = revision;
        QualificationRevision = qualificationRevision;
    }

    public long? Revision { get; }
    public long? QualificationRevision { get; }

    internal static ModerationQualificationResponse FromOutcome(
        Guid operationId, ModerationQualificationWriteResult result) => result.Outcome switch
        {
            ModerationQualificationWriteOutcome.Known when result.Receipt is { } receipt =>
                new(BaseCommandResponse.Success(receipt.SourceId), receipt.ResultRevision, receipt.QualificationRevision),
            ModerationQualificationWriteOutcome.Unauthenticated => new(BaseCommandResponse.Authentication<Guid>()),
            ModerationQualificationWriteOutcome.Denied => new(BaseCommandResponse.Authorization<Guid>()),
            ModerationQualificationWriteOutcome.Conflict => new(BaseCommandResponse.Conflict(operationId)),
            ModerationQualificationWriteOutcome.NotFound => new(BaseCommandResponse.NotFound<Guid>()),
            _ => new(BaseCommandResponse.Failure<Guid>("ModerationAuthorityUnavailable"))
        };

    internal static ModerationQualificationResponse Invalid(IEnumerable<string> errors) =>
        new(BaseCommandResponse.Validation<Guid>(errors));
}
