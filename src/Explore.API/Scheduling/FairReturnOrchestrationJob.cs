using Explore.Application.Contracts.Scheduling;
using Explore.Infrastructure.Waitlist;
using Quartz;

namespace Explore.API.Scheduling;

[DisallowConcurrentExecution]
public sealed class FairReturnOrchestrationJob(
    FairReturnOrchestrationDrainService drainService,
    ILogger<FairReturnOrchestrationJob> logger) :
    IJob
{
    public const string EffectIdKey = "effect_id";

    public async Task Execute(
        IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        string? rawEffectId =
            context.MergedJobDataMap.GetString(
                EffectIdKey);
        Guid? effectId = string.IsNullOrWhiteSpace(
            rawEffectId)
            ? null
            : Guid.TryParse(
                rawEffectId,
                out Guid parsedEffectId)
                ? parsedEffectId
                : throw new InvalidOperationException(
                    "Fair-return effect pointer is invalid.");
        var result = await drainService.DrainAsync(
            effectId,
            context.CancellationToken);
        if (result.Claimed > 0)
        {
            logger.LogInformation(
                "Scheduled job {JobName} drained " +
                "{DrainedCount} fair-return effects.",
                ScheduledJobNames
                    .FairReturnOrchestration,
                result.Claimed);
        }
    }
}
