using System.Collections.Immutable;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public enum ModerationResourceReviewDutyOutcome
{
    Unavailable,
    AwaitingReview,
    Reviewed,
    Escalated,
    AlreadyEscalated,
    Released,
    RecipientUnavailable
}

public sealed record ModerationResourceReviewDutyResult(
    ModerationResourceReviewDutyOutcome Outcome, EventResourceModerationReviewDuty? Duty = null,
    EventResourceModerationReviewCompletion? Completion = null,
    EventResourceModerationReviewEscalation? Escalation = null);

public sealed record ModerationResourceReviewDutyBatch(
    bool Available, Guid? LastScannedQuarantineId, ImmutableArray<ModerationResourceReviewDutyResult> Results);

/// <summary>
/// One bounded tenant pass using the installed persisted maintenance principal. The cursor is
/// progress, not permission; each cause reacquires current native authority in its own transaction.
/// </summary>
public interface IModerationResourceQuarantineDutyRepository
{
    Task<ModerationResourceReviewDutyBatch> ExecutePassAsync(
        Guid? afterQuarantineId, int limit, CancellationToken cancellationToken = default);
}
