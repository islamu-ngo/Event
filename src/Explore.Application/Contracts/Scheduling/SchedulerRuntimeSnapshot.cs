namespace Explore.Application.Contracts.Scheduling;

/// <summary>
/// Point-in-time view of the running scheduler. <paramref name="Available"/> is false when the host runs with
/// scheduling turned off, which is reported as an explicit state rather than an empty job list so operators can
/// tell "nothing is scheduled" apart from "scheduling is disabled".
/// </summary>
public sealed record SchedulerRuntimeSnapshot(
    bool Available,
    string SchedulerName,
    string InstanceId,
    bool Started,
    bool InStandbyMode,
    bool Shutdown,
    bool Clustered,
    bool SupportsPersistence,
    int ExecutingJobCount,
    IReadOnlyList<SchedulerJobSnapshot> Jobs)
{
    public static SchedulerRuntimeSnapshot Unavailable { get; } = new(
        Available: false,
        SchedulerName: string.Empty,
        InstanceId: string.Empty,
        Started: false,
        InStandbyMode: false,
        Shutdown: false,
        Clustered: false,
        SupportsPersistence: false,
        ExecutingJobCount: 0,
        Jobs: []);
}

/// <summary>One scheduled job and every trigger currently attached to it.</summary>
public sealed record SchedulerJobSnapshot(
    string Name,
    string Group,
    string? Owner,
    string? Description,
    bool Durable,
    bool Executing,
    IReadOnlyList<SchedulerTriggerSnapshot> Triggers);

/// <summary>
/// One trigger's identity, lifecycle state, and fire timeline. <paramref name="State"/> is a normalized token
/// rather than a scheduler library enum so the Application layer stays independent of the scheduler in use.
/// </summary>
public sealed record SchedulerTriggerSnapshot(
    string Name,
    string Group,
    string State,
    string? ScheduleSummary,
    DateTimeOffset? NextFireTimeUtc,
    DateTimeOffset? PreviousFireTimeUtc);
