namespace Explore.Application.Contracts.Scheduling;

/// <summary>
/// Operator-facing scheduler control seam. It complements <see cref="IScheduledJobRegistry"/>, which describes the
/// jobs the platform intends to run; this contract reports and manipulates what the scheduler is actually running.
/// Implementations live in the host that owns the scheduler, keeping the library dependency out of Application.
/// </summary>
public interface ISchedulerOperations
{
    Task<SchedulerRuntimeSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);

    /// <summary>Moves the scheduler into standby; running jobs finish but no further triggers fire.</summary>
    Task<SchedulerOperationResult> PauseAllAsync(CancellationToken cancellationToken);

    Task<SchedulerOperationResult> ResumeAllAsync(CancellationToken cancellationToken);

    Task<SchedulerOperationResult> PauseJobAsync(string group, string name, CancellationToken cancellationToken);

    Task<SchedulerOperationResult> ResumeJobAsync(string group, string name, CancellationToken cancellationToken);

    /// <summary>Fires the job once immediately, leaving its existing triggers and schedule untouched.</summary>
    Task<SchedulerOperationResult> TriggerJobAsync(string group, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Clears every trigger of the job that is stuck in the scheduler's error state, returning it to normal
    /// firing. Only an operator can clear that state, which is why the surface that reports it must also offer
    /// this remedy.
    /// </summary>
    Task<SchedulerOperationResult> ResetJobErrorStateAsync(string group, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Requests cancellation of the job's currently executing instances. Interruption is cooperative: the
    /// scheduler signals the running job's cancellation token, and a job that does not observe it keeps running.
    /// The result reports whether an execution was actually signalled rather than implying a forced stop.
    /// </summary>
    Task<SchedulerOperationResult> InterruptJobAsync(string group, string name, CancellationToken cancellationToken);
}
