using Explore.Application.Contracts.Scheduling;
using Explore.Application.Telemetry;

namespace Explore.API.Scheduling;

/// <summary>
/// Writes each scheduler control attempt as a structured log event and records it on the platform metric.
/// <para>
/// Default diagnostics retain action, target category and outcome, without copying the operator reference,
/// job identity or correlation supplied in the audit record. An identity-bearing audit destination must be
/// registered separately from the default diagnostic pipeline.
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
            : "job";

        if (record.Succeeded)
        {
            logger.LogInformation(
                "Scheduler admin action {SchedulerAction} on {SchedulerTarget} succeeded.",
                record.Action,
                target);
        }
        else
        {
            logger.LogWarning(
                "Scheduler admin action {SchedulerAction} on {SchedulerTarget} was refused with {FailureCode}.",
                record.Action,
                target,
                record.FailureCode);
        }

        metrics.RecordSchedulerAdminAction(record.Action, record.Succeeded ? "succeeded" : record.FailureCode ?? "refused");
        return Task.CompletedTask;
    }
}
