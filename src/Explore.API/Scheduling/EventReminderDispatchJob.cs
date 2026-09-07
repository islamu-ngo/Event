using System.Globalization;
using Explore.Application.Contracts.Scheduling;
using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Quartz;

namespace Explore.API.Scheduling;

/// <summary>
/// The trigger carries only durable identifiers as discrete string entries in the <see cref="JobDataMap"/>;
/// message content and transport data stay in the application database, so a stale scheduler row can never
/// resend real content.
/// </summary>
[DisallowConcurrentExecution]
public sealed class EventReminderDispatchJob(
    IEmailDispatchDrainService drainService,
    ILogger<EventReminderDispatchJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!TryReadIdentifier(context, ScheduledDeadlinePointerKeys.TenantId, out var tenantId) ||
            !TryReadIdentifier(context, ScheduledDeadlinePointerKeys.PublishEventId, out var publishEventId))
        {
            logger.LogWarning(
                "Quartz job {JobName} skipped because no usable pointer was supplied.",
                ScheduledJobNames.EventReminderDispatch);
            return;
        }

        var useCase = ReadString(context, ScheduledDeadlinePointerKeys.UseCase);
        if (!string.Equals(useCase, EventLifecycleAutomationUseCases.EventReminder, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Quartz job {JobName} skipped unsupported use case {UseCase}.",
                ScheduledJobNames.EventReminderDispatch,
                useCase);
            return;
        }

        var result = await drainService.ProcessSingleAsync(
            tenantId,
            publishEventId,
            ScheduledJobNames.EventReminderDispatch,
            context.CancellationToken);

        logger.LogInformation(
            "Quartz job {JobName} completed with outcome {Outcome}.",
            ScheduledJobNames.EventReminderDispatch,
            result.Outcome);
    }

    /// <summary>
    /// Reads from the merged map so a value supplied on either the job detail or the trigger is honored.
    /// An absent or unparsable identifier is a poison payload: it is logged and dropped rather than thrown,
    /// because throwing would make the scheduler retry a trigger that can never succeed.
    /// </summary>
    private static bool TryReadIdentifier(IJobExecutionContext context, string key, out Guid value)
    {
        return Guid.TryParse(ReadString(context, key), CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// <c>GetString</c> throws when the key is absent, so the map is probed first: a trigger with no
    /// payload is a recoverable no-op, not an exception the scheduler should retry.
    /// </summary>
    private static string? ReadString(IJobExecutionContext context, string key)
    {
        return context.MergedJobDataMap.TryGetValue(key, out var raw) && raw is string value
            ? value
            : null;
    }
}
