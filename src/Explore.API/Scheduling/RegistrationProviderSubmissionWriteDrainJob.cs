using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Scheduling;
using Explore.Application.Services.Registration.Commands;
using Quartz;

namespace Explore.API.Scheduling;

[DisallowConcurrentExecution]
public sealed class RegistrationProviderSubmissionWriteDrainJob(
    ICommandHandler<DrainRegistrationProviderSubmissionWriteEffectsCommand, int> handler,
    ILogger<RegistrationProviderSubmissionWriteDrainJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        int processed = await handler.ExecuteAsync(
            new DrainRegistrationProviderSubmissionWriteEffectsCommand(
                ScheduledJobNames.RegistrationProviderSubmissionWriteDrain),
            context.CancellationToken);
        logger.LogInformation(
            "Scheduled job {JobName} completed. Processed={ProcessedCount}",
            ScheduledJobNames.RegistrationProviderSubmissionWriteDrain,
            processed);
    }
}
