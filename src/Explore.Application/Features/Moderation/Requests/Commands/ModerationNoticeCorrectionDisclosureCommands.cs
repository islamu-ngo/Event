using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Application.Features.Moderation.Requests.Commands;

/// <summary>Authorable correction text only, not recipient access, delivery or effect completion.</summary>
public sealed record ApproveModerationNoticeCorrectionDisclosureCommand : ModerationQualificationCommand
{
    public required ModerationNoticeDisclosureIntent Intent { get; init; }
    public required string ExpectedPendingSnapshotHash { get; init; }
    public required string ExpectedPendingEffectBindingHash { get; init; }

    internal override ModerationQualificationMutation ToMutation() => new ApproveModerationNoticeCorrectionDisclosureSource
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        Intent = Intent,
        ExpectedPendingSnapshotHash = ExpectedPendingSnapshotHash,
        ExpectedPendingEffectBindingHash = ExpectedPendingEffectBindingHash
    };
}
