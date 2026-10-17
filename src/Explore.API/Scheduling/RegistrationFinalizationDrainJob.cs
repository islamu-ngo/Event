using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Scheduling;
using Explore.Application.Features.RegistrationSubmissions.Commands;
using Quartz;

namespace Explore.API.Scheduling;

/// <summary>
/// Runs one fenced finalization pass. <see cref="DrainRegistrationFinalizationEffectsCommand"/>
/// claims identities under explicit Global authority and executes each order in a
/// fresh ordinary Runtime tenant scope; the timer owns neither authority nor settlement.
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

        // This scope owns queue discovery only. The command owns a fresh Runtime
        // scope per claim, bound before any order persistence service is resolved.
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        var drainHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<DrainRegistrationFinalizationEffectsCommand, int>>();

        var drained = await drainHandler.ExecuteAsync(
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
