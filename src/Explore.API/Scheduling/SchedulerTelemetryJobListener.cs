using System.Diagnostics;
using Explore.Application.Contracts.Scheduling;
using Quartz;
using Quartz.Listener;

namespace Explore.API.Scheduling;

/// <summary>
/// Replaces per-job hand-written completion logging with one cross-cutting observer. Eleven jobs each
/// writing their own "completed" line is duplication that drifts — different fields, different levels, no
/// failure metric at all — whereas a listener is Quartz's supported hook for exactly this and yields one
/// uniform signal for every job, including jobs added later that forget to log.
/// <para>
/// <strong>Every method body is wrapped in try/catch on purpose.</strong> Quartz documents that an
/// unhandled listener exception can disrupt the scheduling cycle, which would turn a telemetry defect into
/// a platform-wide outage where jobs silently stop firing. Containment makes the worst case "we lost a
/// metric", which is the only acceptable failure mode for observability code.
/// </para>
/// </summary>
public sealed class SchedulerTelemetryJobListener(
    ISchedulerJobTelemetry telemetry,
    ILogger<SchedulerTelemetryJobListener> logger) : JobListenerSupport
{
    /// <summary>Key under which the execution's stopwatch timestamp is stashed on the job context.</summary>
    private const string StartTimestampKey = "explore.telemetry.startTimestamp";

    public override string Name => "explore-scheduler-telemetry";

    public override Task JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Stopwatch timestamps rather than context.JobRunTime: the latter is only populated after the
            // job finishes, and is unavailable on the veto path.
            context?.Put(StartTimestampKey, Stopwatch.GetTimestamp());
        }
        catch (Exception exception)
        {
            ReportListenerFault(exception, nameof(JobToBeExecuted));
        }

        return Task.CompletedTask;
    }

    public override Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // A vetoed execution is not a success and not a failure — it never ran. Recording it under its
            // own outcome keeps a trigger listener silently suppressing a job from looking like health.
            Record(context, SchedulerJobOutcomes.Vetoed);
        }
        catch (Exception exception)
        {
            ReportListenerFault(exception, nameof(JobExecutionVetoed));
        }

        return Task.CompletedTask;
    }

    public override Task JobWasExecuted(
        IJobExecutionContext context,
        JobExecutionException? jobException,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Record(context, jobException is null ? SchedulerJobOutcomes.Succeeded : SchedulerJobOutcomes.Failed);

            if (jobException is not null)
            {
                // The exception type only: provider errors carry connection strings, hostnames, and payload
                // fragments that the platform keeps out of logs by convention.
                logger.LogError(
                    "Scheduled job {JobName} in group {JobGroup} failed. FailureType={FailureType}",
                    context?.JobDetail.Key.Name,
                    context?.JobDetail.Key.Group,
                    (jobException.InnerException ?? jobException).GetType().Name);
            }
        }
        catch (Exception exception)
        {
            ReportListenerFault(exception, nameof(JobWasExecuted));
        }

        return Task.CompletedTask;
    }

    private void Record(IJobExecutionContext? context, string outcome)
    {
        if (context is null)
        {
            return;
        }

        telemetry.RecordSchedulerJobExecution(
            context.JobDetail.Key.Name,
            context.JobDetail.Key.Group,
            outcome,
            ReadElapsedSeconds(context));
    }

    /// <summary>
    /// Falls back to zero rather than guessing when the start timestamp is missing, which happens on the
    /// veto path if the veto beat this listener's own <see cref="JobToBeExecuted"/>.
    /// </summary>
    private static double ReadElapsedSeconds(IJobExecutionContext context)
    {
        return context.Get(StartTimestampKey) is long startTimestamp
            ? Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds
            : 0;
    }

    /// <summary>
    /// The last line of defence. If even the fault report throws, the exception is swallowed: nothing this
    /// class does is worth stalling the scheduler for.
    /// </summary>
    private void ReportListenerFault(Exception exception, string listenerMethod)
    {
        try
        {
            logger.LogWarning(
                "Scheduler telemetry listener {ListenerMethod} failed and was contained. FailureType={FailureType}",
                listenerMethod,
                exception.GetType().Name);
        }
        catch (Exception loggingFailure) when (loggingFailure is not OperationCanceledException)
        {
            // Deliberately empty: a broken logging pipeline must not become a stalled scheduling cycle.
        }
    }
}
