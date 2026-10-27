namespace Explore.Application.Contracts.Notifications;

/// <summary>Opaque correction coordinates, never evidence of current recipient or disclosure authority.</summary>
public sealed record ModerationNoticeCorrectionReadRequest(
    Guid PendingEffectId, string ExpectedPendingSnapshotHash, string ExpectedPendingEffectBindingHash,
    Guid ApprovalId, Guid GrantId, Guid CaseId, long ExpectedCaseRevision, Guid OriginalNoticeId)
{
    public override string ToString() => nameof(ModerationNoticeCorrectionReadRequest);
}

public interface IModerationNoticeCorrectionDisclosure
{
    /// <summary>Requalifies the current recipient and correction fences through actual output; never replay the consumer.</summary>
    Task<bool> DiscloseAsync(ModerationNoticeCorrectionReadRequest request,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> write, CancellationToken cancellationToken = default);
}
