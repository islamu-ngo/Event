using Explore.Application.Contracts.Scheduling;
using Explore.Application.Contracts.Services;
using Quartz;

namespace Explore.API.Scheduling;

/// <summary>
/// Runs on its own job key so a slow recovery scan can never block the far more frequent drain job.
/// </summary>
[DisallowConcurrentExecution]
public sealed class EmailDispatchRecoveryScanJob(
    IEmailDispatchDrainService drainService,
    ILogger<EmailDispatchRecoveryScanJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var result = await drainService.RecoverStaleProcessingAsync(context.CancellationToken);
        logger.LogInformation(
            "Quartz job {JobName} completed. Recovered={RecoveredCount}, ProcessingStartedBefore={ProcessingStartedBefore:o}",
            ScheduledJobNames.EmailDispatchRecoveryScan,
            result.RecoveredCount,
            result.ProcessingStartedBefore);
    }
}
