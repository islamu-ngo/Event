using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Scheduling;
using Quartz;

namespace Explore.API.Scheduling;

[DisallowConcurrentExecution]
public sealed class VisitorEnrollmentRecoveryJob(
    ILocalVisitorEnrollmentRecoveryService recovery,
    ILogger<VisitorEnrollmentRecoveryJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var result = await recovery.RecoverOnceAsync(context.CancellationToken);
        logger.LogInformation(
            "Scheduled job {JobName} completed. Observed={ObservedCount}, Settled={SettledCount}",
            ScheduledJobNames.VisitorEnrollmentRecovery, result.ObservedCount, result.SettledCount);
    }
}
