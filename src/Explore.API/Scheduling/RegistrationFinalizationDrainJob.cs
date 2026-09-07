using Explore.Application.Contracts.Scheduling;
using Explore.Application.Features.RegistrationSubmissions.Commands;
using MediatR;
using Quartz;

namespace Explore.API.Scheduling;

/// <summary>
/// Replaces a hand-rolled <c>BackgroundService</c> polling loop. Only the timer moved: the drain's fencing,
/// claim, and retry semantics live in <see cref="DrainRegistrationFinalizationEffectsCommand"/> and are
/// untouched, which is the whole point of migrating this worker first — it is the smallest candidate whose
/// loop carried no logic of its own, so it proves the pattern without putting claim semantics at risk.
/// <para>
/// <see cref="DisallowConcurrentExecutionAttribute"/> preserves the guarantee the <c>while</c> loop gave for
/// free: a slow pass delays the next one instead of overlapping with it.
/// </para>
/// </summary>
[DisallowConcurrentExecution]
public sealed class RegistrationFinalizationDrainJob(
    IServiceScopeFactory scopeFactory,
    ILogger<RegistrationFinalizationDrainJob> logger) : IJob
{
    /// <summary>
    /// Identifies the drain's consumer in durable claim rows. It names the scheduler rather than the deleted
    /// worker so an operator reading a claim can find the thing that actually holds it.
    /// </summary>
    private const string ConsumerId = "registration-finalization-drain-job";

    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // A scope per pass, matching the worker: the drain command resolves scoped persistence services and
        // must not accumulate tracked state across passes.
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var drained = await sender.Send(
            new DrainRegistrationFinalizationEffectsCommand(ConsumerId),
            context.CancellationToken);

        // Silent on an empty pass. At this cadence a per-pass line would be almost entirely noise, and the
        // telemetry listener already records every execution and its duration.
        if (drained > 0)
        {
            logger.LogInformation(
                "Scheduled job {JobName} drained {DrainedCount} registration-finalization effects.",
                ScheduledJobNames.RegistrationFinalizationDrain,
                drained);
        }
    }
}
