using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;
using ISLAMU.Moderation.Application;

namespace Explore.Application.Features.Moderation.Requests.Commands;

/// <summary>Carries lifecycle intent only; the shared service resolves authority inside its store fence.</summary>
public sealed record ApplyModerationLifecycleCommand(ModerationOperation Operation)
    : ICommand<ModerationLifecycleResponse>
{
    public override string ToString() => nameof(ApplyModerationLifecycleCommand);
}

public sealed record ModerationLifecycleResponse : BaseCommandResponse<Guid>
{
    private ModerationLifecycleResponse(
        BaseCommandResponse<Guid> state, Guid? sanctionId = null, long? revision = null)
        : base(state, true)
    {
        SanctionId = sanctionId;
        Revision = revision;
    }

    public Guid? SanctionId { get; }
    /// <summary>The accepted decision revision, or contribution revision for revocation and reconciliation.</summary>
    public long? Revision { get; }

    internal static ModerationLifecycleResponse FromOutcome(
        Guid operationId, ModerationOperationResult result) => result.Outcome switch
        {
            ModerationOperationOutcome.Known when result.DecisionId is { } decisionId && result.Revision is { } revision =>
                new(BaseCommandResponse.Success(decisionId), result.SanctionId, revision),
            ModerationOperationOutcome.Denied => new(BaseCommandResponse.Authorization<Guid>()),
            ModerationOperationOutcome.NotFound => new(BaseCommandResponse.NotFound<Guid>()),
            ModerationOperationOutcome.Conflict => new(BaseCommandResponse.Conflict(operationId)),
            _ => new(BaseCommandResponse.Failure<Guid>("ModerationAuthorityUnavailable"))
        };

    internal static ModerationLifecycleResponse Invalid(IEnumerable<string> errors) =>
        new(BaseCommandResponse.Validation<Guid>(errors));
}
