using Explore.Application.Contracts.Scheduling;
using Explore.Application.Telemetry;

namespace Explore.API.Scheduling;

/// <summary>
/// Writes each scheduler control attempt as a structured log event and records it on the platform metric.
/// <para>
/// Properties are logged as named values rather than interpolated into the message so the host's structured JSON
/// output stays queryable — an operator can ask "who triggered jobs last night" without regex over log text.
/// Refusals are logged at warning level because a denied privileged action deserves more attention than a
/// successful routine one.
/// </para>
/// </summary>
public sealed class LoggingSchedulerAdminAuditSink(
    ILogger<LoggingSchedulerAdminAuditSink> logger,
    BusinessMetrics metrics) : ISchedulerAdminAuditSink
{
    public Task RecordAsync(SchedulerAdminAuditRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        var target = record.JobName is null
            ? "scheduler"
            : $"{record.JobGroup}.{record.JobName}";

        if (record.Succeeded)
        {
            logger.LogInformation(
                "Scheduler admin action {SchedulerAction} on {SchedulerTarget} by {PrincipalReference} succeeded. CorrelationId={CorrelationId}",
                record.Action,
                target,
                record.PrincipalReference,
                record.CorrelationId);
        }
        else
        {
            logger.LogWarning(
                "Scheduler admin action {SchedulerAction} on {SchedulerTarget} by {PrincipalReference} was refused with {FailureCode}. CorrelationId={CorrelationId}",
                record.Action,
                target,
                record.PrincipalReference,
                record.FailureCode,
                record.CorrelationId);
        }

        metrics.RecordSchedulerAdminAction(record.Action, record.Succeeded ? "succeeded" : record.FailureCode ?? "refused");
        return Task.CompletedTask;
    }
}
