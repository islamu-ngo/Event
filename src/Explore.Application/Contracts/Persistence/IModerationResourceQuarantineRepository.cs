using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Executes one originally selected native resource quarantine under the actual maintenance
/// lease. Coordinates are intent, never authority. The owner rereads original purpose and
/// current native adoption; its receipt proves only this cause, not the complete effect recipe.
/// </summary>
public interface IModerationResourceQuarantineRepository
{
    /// <summary>
    /// Discovers original quarantine coordinates under the exact live native lease, then
    /// independently executes every selected cause, including exact retired dispositions.
    /// Accounting is complete only when each original ordinal has its native receipt; it is
    /// never effect-recipe completion. Failed discovery supplies no partial target selection.
    /// </summary>
    Task<ModerationOriginalQuarantineResult> ExecuteOriginalAsync(
        Guid effectId, Guid leaseId, long expectedEffectRevision, CancellationToken cancellationToken = default);

    Task<ModerationResourceQuarantineResult> ExecuteAsync(
        Guid effectId, Guid leaseId, long expectedEffectRevision, int bindingOrdinal,
        Guid resourceId, Guid materialVersionId, CancellationToken cancellationToken = default);
}

public enum ModerationResourceQuarantineOutcome
{
    Unavailable,
    Applied,
    AlreadyApplied,
    Conflict,
    /// <summary>The exact original cause was explicitly retired; this never reapplies its guard.</summary>
    Retired
}

public sealed record ModerationResourceQuarantineResult(
    ModerationResourceQuarantineOutcome Outcome, EventResourceModerationQuarantine? Receipt = null);

public enum ModerationOriginalQuarantineOutcome
{
    Unavailable,
    Partial,
    Accounted
}

/// <summary>
/// Accounts for native causes only. Expected count comes from the qualified original, never
/// caller intent. Retired receipts remain in this set without restoring their guards.
/// Notices, protected response, completed review and terminal disclosure require separate proof.
/// </summary>
public sealed record ModerationOriginalQuarantineResult(
    ModerationOriginalQuarantineOutcome Outcome,
    int ExpectedCauseCount,
    IReadOnlyList<EventResourceModerationQuarantine> Receipts);
