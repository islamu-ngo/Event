using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;

namespace Explore.Application.Features.Moderation.Requests.Commands;

public sealed record PrepareModerationConsumerMeasurementCommand : ModerationQualificationCommand
{
    public ModerationOriginalEffectAnchor? OriginalEffect { get; init; }
    public required Guid ChallengeId { get; init; }
    public required Guid InstallationId { get; init; }
    public required long ExpectedInstallationRevision { get; init; }
    public required string ConsumerId { get; init; }
    public required ModerationActivationBinding Binding { get; init; }
    public required string ExpectedRuntimeGeneration { get; init; }
    public required string ExpectedInventoryFingerprint { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
    internal override ModerationQualificationMutation ToMutation() => new PrepareModerationConsumerMeasurementSource
    {
        OperationId = OperationId, Target = Target, ExpectedQualificationRevision = ExpectedQualificationRevision,
        OriginalEffect = OriginalEffect,
        ChallengeId = ChallengeId, InstallationId = InstallationId, ExpectedInstallationRevision = ExpectedInstallationRevision,
        ConsumerId = ConsumerId, Binding = Binding, ExpectedRuntimeGeneration = ExpectedRuntimeGeneration,
        ExpectedInventoryFingerprint = ExpectedInventoryFingerprint, ValidUntil = ValidUntil
    };
}
