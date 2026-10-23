namespace Explore.Application.Contracts.Scheduling;

/// <summary>
/// Receives an audit record for every attempted scheduler control action.
/// <para>
/// The default implementation emits bounded action/outcome diagnostics, not the identity-bearing record,
/// into the ordinary logging pipeline. An access-controlled audit destination can receive the full record
/// by registering a different sink without changing scheduler call sites.
/// </para>
/// </summary>
public interface ISchedulerAdminAuditSink
{
    /// <summary>
    /// Records one attempted action. Refusals are recorded as deliberately as successes: a denied privileged
    /// action is often the more interesting security event.
    /// </summary>
    Task RecordAsync(SchedulerAdminAuditRecord record, CancellationToken cancellationToken);
}

/// <summary>
/// One audited scheduler control attempt. Carries operational identifiers only — never job data maps, trigger
/// payloads, tenant content, or raw exception text.
/// </summary>
/// <param name="Action">Closed-vocabulary action token, e.g. <c>pause-scheduler</c> or <c>interrupt-job</c>.</param>
/// <param name="PrincipalReference">Stable reference to the acting operator, for accountability.</param>
/// <param name="JobGroup">Target job group, or null for instance-wide actions.</param>
/// <param name="JobName">Target job name, or null for instance-wide actions.</param>
/// <param name="Succeeded">Whether the scheduler accepted the action.</param>
/// <param name="FailureCode">Structured refusal reason when the action was not accepted.</param>
/// <param name="CorrelationId">Request correlation identifier, tying the record to traces and logs.</param>
/// <param name="OccurredAtUtc">When the attempt was evaluated.</param>
public sealed record SchedulerAdminAuditRecord(
    string Action,
    string PrincipalReference,
    string? JobGroup,
    string? JobName,
    bool Succeeded,
    string? FailureCode,
    string? CorrelationId,
    DateTime OccurredAtUtc);

/// <summary>Closed vocabulary of audited scheduler actions. Values appear in audit records and metrics.</summary>
public static class SchedulerAdminAuditActions
{
    public const string PauseScheduler = "pause-scheduler";
    public const string ResumeScheduler = "resume-scheduler";
    public const string PauseJob = "pause-job";
    public const string ResumeJob = "resume-job";
    public const string TriggerJob = "trigger-job";
    public const string ResetJobErrorState = "reset-job-error-state";
    public const string InterruptJob = "interrupt-job";
}
