namespace Explore.Application.Contracts.Persistence;

public enum ModerationNoticeCleanupOutcome
{
    Applied = 1,
    Denied = 2,
    Unavailable = 3
}

/// <summary>
/// A bounded scan, not an effect-completion or notification receipt. Missing acknowledgement
/// remains unproven after cleanup. Only Applied permits advancing LastScannedApprovalId.
/// An unavailable/ambiguous commit must be retried from the caller's previous cursor.
/// </summary>
public sealed record ModerationNoticeCleanupResult(
    ModerationNoticeCleanupOutcome Outcome, Guid? LastScannedApprovalId = null,
    int Scanned = 0, int Purged = 0, int OriginalWritesProven = 0);

public interface IModerationNoticeCleanupRepository
{
    /// <summary>
    /// Scans at most limit original disclosure approvals in native ID order, including live rows
    /// so an unexpired head cannot starve later expired records. Start a new sweep at null after
    /// an Applied empty page. The installed maintenance credential and current tenant supply
    /// authority; the cursor supplies none. No recognition retention policy is consulted.
    /// </summary>
    Task<ModerationNoticeCleanupResult> PruneAsync(
        Guid? afterApprovalId, int limit, CancellationToken cancellationToken = default);
}
