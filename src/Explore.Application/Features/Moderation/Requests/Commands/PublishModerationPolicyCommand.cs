using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Responses;

namespace Explore.Application.Features.Moderation.Requests.Commands;

/// <summary>Complete immutable authoring intent; current source and active evidence belong to the native writer.</summary>
public sealed record PublishModerationPolicyCommand(ModerationPolicyPublicationRequest Request)
    : ICommand<ModerationPolicyPublicationResponse>
{
    public override string ToString() => nameof(PublishModerationPolicyCommand);
}

public sealed record ModerationPolicyPublicationResponse : BaseCommandResponse<Guid>
{
    private ModerationPolicyPublicationResponse(
        BaseCommandResponse<Guid> state, Guid? publicationId = null, long? revision = null,
        bool? publishedActive = null) : base(state, true)
    {
        PublicationId = publicationId;
        Revision = revision;
        PublishedActive = publishedActive;
    }

    public Guid? PublicationId { get; }
    public long? Revision { get; }
    /// <summary>Publication-time state, never a grant or proof of current deployment conformance.</summary>
    public bool? PublishedActive { get; }

    internal static ModerationPolicyPublicationResponse FromOutcome(
        Guid operationId, ModerationPolicyPublicationResult result) => result.Outcome switch
        {
            ModerationPolicyPublicationOutcome.Known when result.Publication is { } publication =>
                new(BaseCommandResponse.Success(publication.PolicyId), publication.Id,
                    publication.Revision, publication.PublishedActive),
            ModerationPolicyPublicationOutcome.Unauthenticated => new(BaseCommandResponse.Authentication<Guid>()),
            ModerationPolicyPublicationOutcome.Denied => new(BaseCommandResponse.Authorization<Guid>()),
            ModerationPolicyPublicationOutcome.Conflict => new(BaseCommandResponse.Conflict(operationId)),
            ModerationPolicyPublicationOutcome.Invalid => Invalid(["Moderation publication intent is invalid."]),
            _ => new(BaseCommandResponse.Failure<Guid>("ModerationPublicationUnavailable"))
        };

    internal static ModerationPolicyPublicationResponse Invalid(IEnumerable<string> errors) =>
        new(BaseCommandResponse.Validation<Guid>(errors));
}
