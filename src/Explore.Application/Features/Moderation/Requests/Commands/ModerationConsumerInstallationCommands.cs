using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;

namespace Explore.Application.Features.Moderation.Requests.Commands;

public sealed record InstallModerationConsumerCommand : ModerationQualificationCommand
{
    public required Guid InstallationId { get; init; }
    public required string ConsumerId { get; init; }
    public required ModerationNativeMode Mode { get; init; }
    public required string ExpectedRuntimeGeneration { get; init; }
    public required string ExpectedInventoryFingerprint { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
    internal override ModerationQualificationMutation ToMutation() => new InstallModerationConsumerSource
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        InstallationId = InstallationId,
        ConsumerId = ConsumerId,
        Mode = Mode,
        ExpectedRuntimeGeneration = ExpectedRuntimeGeneration,
        ExpectedInventoryFingerprint = ExpectedInventoryFingerprint,
        ValidUntil = ValidUntil
    };
}

public sealed record WithdrawModerationConsumerCommand : ModerationQualificationCommand
{
    public required Guid InstallationId { get; init; }
    public required long ExpectedInstallationRevision { get; init; }
    internal override ModerationQualificationMutation ToMutation() => new WithdrawModerationConsumerSource
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        InstallationId = InstallationId,
        ExpectedInstallationRevision = ExpectedInstallationRevision
    };
}
