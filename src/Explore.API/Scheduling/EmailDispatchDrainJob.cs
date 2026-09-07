using Explore.Application.Contracts.Scheduling;
using Explore.Application.Contracts.Services;
using Quartz;

namespace Explore.API.Scheduling;

/// <summary>
/// <see cref="DisallowConcurrentExecutionAttribute"/> is the scheduler-enforced replacement for a manual
/// "skip if already running" guard: Quartz will not start a second execution for this job key.
/// </summary>
[DisallowConcurrentExecution]
public sealed class EmailDispatchDrainJob(
    IEmailDispatchDrainService drainService,
    ILogger<EmailDispatchDrainJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var result = await drainService.ProcessBatchAsync(context.CancellationToken);
        logger.LogInformation(
            "Quartz job {JobName} completed. Pending={PendingCount}, Processed={ProcessedCount}, Sent={SentCount}, RetryScheduled={RetryScheduledCount}, DeadLettered={DeadLetteredCount}, Unknown={UnknownCount}, TenantPaused={TenantPausedCount}, AlreadyClaimed={AlreadyClaimedCount}",
            ScheduledJobNames.EmailDispatchDrain,
            result.PendingCount,
            result.ProcessedCount,
            result.SentCount,
            result.RetryScheduledCount,
            result.DeadLetteredCount,
            result.UnknownCount,
            result.TenantPausedCount,
            result.AlreadyClaimedCount);
    }
}
