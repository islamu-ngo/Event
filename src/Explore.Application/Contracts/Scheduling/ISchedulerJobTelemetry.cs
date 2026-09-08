namespace Explore.Application.Contracts.Scheduling;

/// <summary>
/// The scheduler needs exactly one thing from telemetry, so it depends on exactly that rather than on the
/// whole metrics surface. Keeping the seam this narrow is also what makes the listener's containment
/// guarantee testable: a fault can be injected at the one call the listener actually makes.
/// </summary>
public interface ISchedulerJobTelemetry
{
    /// <summary>
    /// Records one execution. Implementations must bound label cardinality and must never accept tenant
    /// identity or payload values — scheduler payloads point at tenant-scoped aggregates, and metric labels
    /// are exported and retained far more widely than logs.
    /// </summary>
    void RecordSchedulerJobExecution(string? jobName, string? jobGroup, string? outcome, double durationSeconds);
}
