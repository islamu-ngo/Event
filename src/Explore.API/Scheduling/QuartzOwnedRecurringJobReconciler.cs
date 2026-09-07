using Quartz;

namespace Explore.API.Scheduling;

public sealed class QuartzOwnedRecurringJobReconciler(
    ISchedulerFactory schedulerFactory,
    QuartzRecurringJobManifest manifest,
    ILogger<QuartzOwnedRecurringJobReconciler> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        IScheduler scheduler = await schedulerFactory.GetScheduler(cancellationToken);

        foreach (JobKey jobKey in manifest.Owned.Except(manifest.Desired))
        {
            TriggerKey triggerKey = QuartzSchedulerKeys.RecurringTriggerFor(jobKey);
            bool triggerRemoved = await scheduler.UnscheduleJob(triggerKey, cancellationToken);
            bool jobRemoved = await scheduler.DeleteJob(jobKey, cancellationToken);

            if (triggerRemoved || jobRemoved)
            {
                logger.LogInformation(
                    "Removed disabled or retired Quartz definition. JobName={JobName}, JobGroup={JobGroup}",
                    jobKey.Name,
                    jobKey.Group);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
