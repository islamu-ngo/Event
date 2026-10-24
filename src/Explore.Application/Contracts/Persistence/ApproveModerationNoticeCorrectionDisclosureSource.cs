using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Approves text for an authenticated pending Revoke correction; audience and original retention remain source-owned.</summary>
public sealed record ApproveModerationNoticeCorrectionDisclosureSource : ModerationQualificationMutation
{
    public required ModerationNoticeDisclosureIntent Intent { get; init; }
    public required string ExpectedPendingSnapshotHash { get; init; }
    public required string ExpectedPendingEffectBindingHash { get; init; }
}
