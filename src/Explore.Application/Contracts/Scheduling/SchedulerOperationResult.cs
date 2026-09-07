namespace Explore.Application.Contracts.Scheduling;

public enum SchedulerOperationOutcome
{
    /// <summary>The scheduler accepted the action.</summary>
    Succeeded = 1,

    /// <summary>Scheduling is disabled for this host, so there is no scheduler to act on.</summary>
    SchedulerUnavailable = 2,

    /// <summary>The addressed job is not present in the scheduler's store.</summary>
    JobNotFound = 3,

    /// <summary>The host is configured read-only, so mutating actions are refused before reaching the scheduler.</summary>
    ReadOnly = 4,

    /// <summary>
    /// The job exists but the action no longer applies to its current state — nothing was executing to interrupt,
    /// or no trigger was in the error state. Reported distinctly so a no-op is never presented as a success.
    /// </summary>
    NotApplicable = 5
}

public sealed record SchedulerOperationResult(SchedulerOperationOutcome Outcome)
{
    public static SchedulerOperationResult Succeeded { get; } = new(SchedulerOperationOutcome.Succeeded);

    public static SchedulerOperationResult SchedulerUnavailable { get; } = new(SchedulerOperationOutcome.SchedulerUnavailable);

    public static SchedulerOperationResult JobNotFound { get; } = new(SchedulerOperationOutcome.JobNotFound);

    public static SchedulerOperationResult ReadOnly { get; } = new(SchedulerOperationOutcome.ReadOnly);

    public static SchedulerOperationResult NotApplicable { get; } = new(SchedulerOperationOutcome.NotApplicable);

    public bool IsSuccess => Outcome == SchedulerOperationOutcome.Succeeded;
}
