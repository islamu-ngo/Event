using Explore.Application.Contracts.Scheduling;
using Explore.Application.Services.Registration.Commands;
using MediatR;
using Quartz;

namespace Explore.API.Scheduling;

[DisallowConcurrentExecution]
public sealed class RegistrationProviderSubmissionWriteDrainJob(
    ISender sender,
    ILogger<RegistrationProviderSubmissionWriteDrainJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        int processed = await sender.Send(
            new DrainRegistrationProviderSubmissionWriteEffectsCommand(
                ScheduledJobNames.RegistrationProviderSubmissionWriteDrain),
            context.CancellationToken);
        logger.LogInformation(
            "Scheduled job {JobName} completed. Processed={ProcessedCount}",
            ScheduledJobNames.RegistrationProviderSubmissionWriteDrain,
            processed);
    }
}
