using Explore.Application.Contracts.Scheduling;
using Explore.Application.Services.Registration;
using Quartz;

namespace Explore.API.Scheduling;

[DisallowConcurrentExecution]
public sealed class RegistrationProviderSubscriptionLifecycleDrainJob(
    RegistrationProviderSubscriptionLifecycleService service,
    ILogger<RegistrationProviderSubscriptionLifecycleDrainJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        int processed = await service.DrainOnceAsync(context.CancellationToken);
        logger.LogInformation(
            "Scheduled job {JobName} completed. Processed={ProcessedCount}",
            ScheduledJobNames.RegistrationProviderSubscriptionLifecycleDrain,
            processed);
    }
}
