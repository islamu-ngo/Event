namespace Explore.Domain.Ai;

public enum AiRunStatus
{
    Queued = 1,
    InProgress = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5
}
