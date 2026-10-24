using System.Collections.Immutable;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Contracts.Persistence;

public enum ModerationScheduleRiskReconciliationOutcome
{
    Unavailable,
    AwaitingDeadline,
    Overdue,
    Terminal
}

public sealed record ModerationScheduleRiskReconciliationResult(
    ModerationScheduleRiskReconciliationOutcome Outcome, ModerationScheduleRiskReview? Review = null);

public sealed record ModerationScheduleRiskReconciliationBatch(
    bool Available, Guid? LastScannedReviewId, ImmutableArray<ModerationScheduleRiskReconciliationResult> Results);

/// <summary>
/// Materializes explicit review deadlines under installed native maintenance custody.
/// This metadata-only pass neither admits event risk nor completes adopted fallback,
/// notification, credential, disclosure or settlement work.
/// </summary>
public interface IModerationScheduleRiskReviewReconciliationRepository
{
    Task<ModerationScheduleRiskReconciliationBatch> ReconcileScheduleRiskAsync(
        Guid? afterReviewId, int limit, CancellationToken cancellationToken = default);
}
