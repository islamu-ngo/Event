using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;

namespace Explore.Application.Features.Moderation.Requests.Commands;

/// <summary>Accepts an authenticated historical assertion, never caller-provided conformance or activation authority.</summary>
public sealed record AcceptModerationConsumerMeasurementCommand : ModerationQualificationCommand
{
    public ModerationOriginalEffectAnchor? OriginalEffect { get; init; }
    public required Guid ReceiptId { get; init; }
    public required Guid ChallengeId { get; init; }
    public required Guid IssuerEnrollmentId { get; init; }
    public required ModerationActivationBinding Binding { get; init; }
    public required string SignedToken { get; init; }
    internal override ModerationQualificationMutation ToMutation() => new AcceptModerationConsumerMeasurementSource
    {
        OperationId = OperationId, Target = Target, ExpectedQualificationRevision = ExpectedQualificationRevision,
        OriginalEffect = OriginalEffect,
        ReceiptId = ReceiptId, ChallengeId = ChallengeId, IssuerEnrollmentId = IssuerEnrollmentId,
        Binding = Binding, SignedToken = SignedToken
    };
}
