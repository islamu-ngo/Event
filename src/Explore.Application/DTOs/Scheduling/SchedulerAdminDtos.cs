namespace Explore.Application.DTOs.Scheduling;

/// <summary>
/// Snapshot of the instance scheduler for operator tooling. When <see cref="Available"/> is false the host runs
/// with scheduling disabled and every collection is empty; clients render that as an explicit disabled state
/// rather than as a healthy scheduler with no work.
/// </summary>
public sealed record SchedulerAdminOverviewDto
{
    public DateTime GeneratedAtUtc { get; init; }
    public bool Available { get; init; }

    /// <summary>True when the host refuses mutating scheduler actions regardless of caller permissions.</summary>
    public bool ReadOnly { get; init; }

    public string SchedulerName { get; init; } = string.Empty;
    public string InstanceId { get; init; } = string.Empty;

    /// <summary>Normalized lifecycle token: <c>running</c>, <c>standby</c>, <c>shutdown</c>, or <c>disabled</c>.</summary>
    public string State { get; init; } = string.Empty;

    public bool Clustered { get; init; }

    /// <summary>
    /// True when the executing-job view and the interrupt action reflect only the node that served this request.
    /// Executing-job reads and interruption are answered per node, not across a cluster, so a clustered
    /// deployment must not read "not running" as "not running anywhere".
    /// </summary>
    public bool ExecutingViewIsNodeLocal { get; init; }

    /// <summary>Count of jobs whose triggers are in the scheduler's error state; drives readiness reporting.</summary>
    public int ErroredJobCount { get; init; }
    public bool SupportsPersistence { get; init; }
    public int ExecutingJobCount { get; init; }

    /// <summary>Number of jobs in the scheduler's store; the rows themselves are a separate collection resource.</summary>
    public int JobCount { get; init; }

    public int PausedJobCount { get; init; }

    /// <summary>Jobs the platform declares but has not scheduled, surfaced so gaps are visible to operators.</summary>
    public IReadOnlyList<string> PlannedJobs { get; init; } = [];
}

public sealed record SchedulerAdminJobDto
{
    public string Name { get; init; } = string.Empty;
    public string Group { get; init; } = string.Empty;

    /// <summary>Owning subsystem from the platform job catalog; null for a job with no registry entry.</summary>
    public string? Owner { get; init; }

    public string? Description { get; init; }
    public bool Durable { get; init; }
    public bool Executing { get; init; }

    /// <summary>Aggregate of the job's trigger states: <c>paused</c> when every trigger is paused.</summary>
    public string State { get; init; } = string.Empty;

    public DateTimeOffset? NextFireTimeUtc { get; init; }
    public DateTimeOffset? PreviousFireTimeUtc { get; init; }
    public IReadOnlyList<SchedulerAdminTriggerDto> Triggers { get; init; } = [];
}

public sealed record SchedulerAdminTriggerDto
{
    public string Name { get; init; } = string.Empty;
    public string Group { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;

    /// <summary>Human-readable cadence, such as a cron expression or a repeat interval.</summary>
    public string? ScheduleSummary { get; init; }

    public DateTimeOffset? NextFireTimeUtc { get; init; }
    public DateTimeOffset? PreviousFireTimeUtc { get; init; }
}
