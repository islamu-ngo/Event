using Explore.Application.Contracts.Waitlist;
using Explore.Application.Telemetry;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Waitlist;

public sealed class FairReturnOrchestrationDrainService(
    IFairReturnOrchestrationRepository repository,
    IFairReturnOrchestrationDispatcher dispatcher,
    IOptions<
        FairReturnOrchestrationDrainSettings> options,
    TimeProvider timeProvider)
{
    public async Task<
        FairReturnOrchestrationDrainResult> DrainAsync(
            Guid? effectId,
            CancellationToken cancellationToken)
    {
        FairReturnOrchestrationDrainSettings settings =
            options.Value;
        DateTime now =
            timeProvider.GetUtcNow().UtcDateTime;
        string leaseOwner =
            $"{Environment.MachineName}:" +
            $"{Environment.ProcessId}";
        IReadOnlyList<
            FairReturnOrchestrationClaim> claims =
            await repository.TryClaimDueAsync(
                now,
                leaseOwner,
                effectId,
                settings.BatchSize,
                settings.MaximumEffectsPerTenant,
                TimeSpan.FromSeconds(
                    settings.LeaseDurationSeconds),
                cancellationToken);
        int succeeded = 0;
        int retryScheduled = 0;
        int unknown = 0;
        int poisoned = 0;
        int deadLettered = 0;
        int staleLease = 0;
        foreach (
            FairReturnOrchestrationClaim claim
            in claims)
        {
            FairReturnOrchestrationDispatchResult
                result = await dispatcher.TryDispatch(
                    claim,
                    cancellationToken);
            switch (result.Outcome)
            {
                case FairReturnDispatchOutcome.Succeeded:
                    succeeded++;
                    break;
                case FairReturnDispatchOutcome
                    .RetryScheduled:
                    retryScheduled++;
                    break;
                case FairReturnDispatchOutcome.Unknown:
                    unknown++;
                    break;
                case FairReturnDispatchOutcome.Poisoned:
                    poisoned++;
                    break;
                case FairReturnDispatchOutcome
                    .DeadLettered:
                    deadLettered++;
                    break;
                case FairReturnDispatchOutcome
                    .StaleLease:
                    staleLease++;
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unsupported fair-return outcome.");
            }
        }
        var drainResult =
            new FairReturnOrchestrationDrainResult(
                claims.Count,
                succeeded,
                retryScheduled,
                unknown,
                poisoned,
                deadLettered,
                staleLease);
        FairReturnOrchestrationTelemetry.Record(
            drainResult);
        return drainResult;
    }
}
