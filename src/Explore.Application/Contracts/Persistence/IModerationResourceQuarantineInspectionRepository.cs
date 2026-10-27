namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Coordinates, not permission. Adoption must name the second receipt of a current fresh
/// material-safety purpose; the current human must be one of its two qualified participants.
/// </summary>
public sealed record ModerationResourceQuarantineInspectionIntent(
    Guid QuarantineId,
    Guid ResourceId,
    Guid MaterialVersionId,
    Guid CaseId,
    long ExpectedCaseRevision,
    Guid AdoptionId,
    Guid ReviewerAssignmentId,
    long ExpectedReviewerRevision,
    long ExpectedQualificationRevision)
{
    public override string ToString() => nameof(ModerationResourceQuarantineInspectionIntent);
}

/// <summary>
/// Borrows each chunk only during the callback; the owner zeroes it when disclosure ends. Native authority and erasure
/// fences remain held through the awaited callback. Consumers must not retain or defer bytes.
/// A failed or cancelled callback is never retried.
/// </summary>
public delegate Task ModerationResourceInspectionOutput(
    ReadOnlyMemory<byte> material, bool destination, CancellationToken cancellationToken);

public interface IModerationResourceQuarantineInspectionRepository
{
    Task<bool> DiscloseAsync(ModerationResourceQuarantineInspectionIntent intent,
        ModerationResourceInspectionOutput write, CancellationToken cancellationToken);
}
