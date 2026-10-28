using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Current resource-case coordinates, not a material inspection or historical allegation grant.</summary>
public sealed record ModerationResourceQuarantineStateIntent(
    Guid ResourceId, Guid MaterialVersionId, Guid CaseId, long ExpectedCaseRevision,
    Guid ReviewerAssignmentId, long ExpectedReviewerRevision, long ExpectedQualificationRevision,
    int Page = 1)
{
    public override string ToString() => nameof(ModerationResourceQuarantineStateIntent);
}

public sealed record ModerationResourceQuarantineStateEntry(
    EventResourceModerationQuarantine Cause,
    EventResourceModerationReviewDuty? Duty,
    EventResourceModerationReviewCompletion? Completion,
    EventResourceModerationQuarantineRelease? Release);

/// <summary>Native entities stay inside the handler; the terminal callback must await actual output.</summary>
public delegate Task<bool> ModerationResourceQuarantineStateOutput(
    IReadOnlyList<ModerationResourceQuarantineStateEntry> entries, bool hasNext,
    Func<CancellationToken, Task<bool>> current, CancellationToken cancellationToken);

public interface IModerationResourceQuarantineStateRepository
{
    Task<bool> DiscloseAsync(ModerationResourceQuarantineStateIntent intent,
        ModerationResourceQuarantineStateOutput write, CancellationToken cancellationToken);
}
