using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.Moderation.Requests.Commands;

/// <summary>Receipt-bound allegation intent only; tenancy, caller, relationship and read permission are server-owned.</summary>
public sealed record SubmitModerationAllegationCommand : ICommand<ModerationAllegationResponse>
{
    private byte[] _presentedToken = [];
    private byte[] _narrative = [];

    public required Guid OperationId { get; init; }
    public required Guid ReceiptId { get; init; }
    /// <summary>Exact bytes qualified as Material during receipt issuance, not replacement narrative text.</summary>
    public required ReadOnlyMemory<byte> Narrative
    {
        get => _narrative.ToArray();
        init => _narrative = value.ToArray();
    }
    public required ReadOnlyMemory<byte> PresentedToken
    {
        get => _presentedToken.ToArray();
        init => _presentedToken = value.ToArray();
    }

    internal ModerationCaseAccessRequest ToRequest() => new(
        ModerationCaseAccessPurpose.SubmitAllegation, Guid.Empty, OperationId, 0,
        ReceiptId, PresentedToken) { Narrative = Narrative };

    public override string ToString() => nameof(SubmitModerationAllegationCommand);
}

/// <summary>Reporter-owned acceptance only. Canonical case identity and revision never cross this boundary.</summary>
public sealed record ModerationAllegationResponse : BaseCommandResponse<Guid>
{
    private ModerationAllegationResponse(
        BaseCommandResponse<Guid> state, DateTimeOffset? acceptedAt = null) : base(state, true)
    {
        AcceptedAt = acceptedAt;
    }

    public DateTimeOffset? AcceptedAt { get; }

    internal static ModerationAllegationResponse FromOutcome(Guid operationId, ModerationCaseIntakeCommitResult result) =>
        result.Outcome switch
        {
            ModerationCaseIntakeCommitOutcome.Accepted when result.Acknowledgement is { } original =>
                new(BaseCommandResponse.Success(original.Id), original.AcceptedAt),
            ModerationCaseIntakeCommitOutcome.Conflict => new(BaseCommandResponse.Conflict(operationId)),
            _ => new(BaseCommandResponse.Failure<Guid>("ModerationCaseIntakeUnavailable"))
        };

    internal static ModerationAllegationResponse Invalid(IEnumerable<string> errors) =>
        new(BaseCommandResponse.Validation<Guid>(errors));
}
