using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Responses;

namespace Explore.Application.Features.Moderation.Requests.Commands;

/// <summary>Freezes the exact intended active revision for measurement; it supplies no active policy.</summary>
public sealed record PrepareModerationPolicyPublicationCommand(
    ModerationPolicyPublicationRequest Request, DateTimeOffset ValidUntil)
    : ICommand<ModerationPolicyPublicationCandidateResponse>
{
    public override string ToString() => nameof(PrepareModerationPolicyPublicationCommand);
}

public sealed record ModerationPolicyPublicationCandidateResponse : BaseCommandResponse<Guid>
{
    private ModerationPolicyPublicationCandidateResponse(
        BaseCommandResponse<Guid> state, long? revision = null, DateTimeOffset? validUntil = null)
        : base(state, true) => (Revision, ValidUntil) = (revision, validUntil);

    public long? Revision { get; }
    public DateTimeOffset? ValidUntil { get; }

    internal static ModerationPolicyPublicationCandidateResponse FromOutcome(
        Guid operationId, ModerationPolicyPublicationCandidateResult result) => result.Outcome switch
        {
            ModerationPolicyPublicationOutcome.Known when result.Candidate is { } candidate =>
                new(BaseCommandResponse.Success(candidate.Id), candidate.ExpectedRevision + 1, candidate.ValidUntil),
            ModerationPolicyPublicationOutcome.Unauthenticated => new(BaseCommandResponse.Authentication<Guid>()),
            ModerationPolicyPublicationOutcome.Denied => new(BaseCommandResponse.Authorization<Guid>()),
            ModerationPolicyPublicationOutcome.Conflict => new(BaseCommandResponse.Conflict(operationId)),
            ModerationPolicyPublicationOutcome.Invalid => Invalid(["Moderation publication preparation is invalid."]),
            _ => new(BaseCommandResponse.Failure<Guid>("ModerationPublicationPreparationUnavailable"))
        };

    internal static ModerationPolicyPublicationCandidateResponse Invalid(IEnumerable<string> errors) =>
        new(BaseCommandResponse.Validation<Guid>(errors));
}
