using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Names one native review chain; possession of its receipt identity grants no authority.</summary>
public sealed record ModerationResourceQuarantineReviewStateIntent
{
    public required Guid ResourceId { get; init; }
    public required Guid MaterialVersionId { get; init; }
    public required Guid QuarantineId { get; init; }
    public required Guid TipId { get; init; }
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required Guid ReviewerAssignmentId { get; init; }
    public required long ExpectedReviewerRevision { get; init; }
    public required long ExpectedQualificationRevision { get; init; }
    public override string ToString() => nameof(ModerationResourceQuarantineReviewStateIntent);
}

public sealed record ModerationResourceQuarantineReviewStateEntry(
    IReadOnlyList<EventResourceModerationQuarantineReview> Chain,
    IReadOnlyList<ResourceSafetyReviewStep> AllowedSteps, bool CanInspect, bool CanRelease,
    DateTimeOffset ValidUntil);

public delegate Task<bool> ModerationResourceQuarantineReviewStateOutput(
    ModerationResourceQuarantineReviewStateEntry entry,
    Func<CancellationToken, Task<bool>> current, CancellationToken cancellationToken);

public interface IModerationResourceQuarantineReviewStateRepository
{
    Task<bool> DiscloseAsync(ModerationResourceQuarantineReviewStateIntent intent,
        ModerationResourceQuarantineReviewStateOutput write, CancellationToken cancellationToken);
}
