using System.Diagnostics.Metrics;
using Explore.Application.Contracts.Waitlist;

namespace Explore.Application.Telemetry;

public static class FairReturnOrchestrationTelemetry
{
    public const long ZeroSentinel = 0;
    public const string MeterIdentity =
        "Explore.FairReturn";

    private static readonly Meter Meter =
        new(MeterIdentity);
    private static readonly Counter<long>
        EffectOutcomes = Meter.CreateCounter<long>(
            "fair_return.orchestration.effects",
            unit: "{effect}");

    public static void Record(
        FairReturnOrchestrationDrainResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        foreach (FairReturnDispatchOutcome outcome
                 in Enum.GetValues<
                     FairReturnDispatchOutcome>())
        {
            EffectOutcomes.Add(
                result.Count(outcome),
                new KeyValuePair<
                    string,
                    object?>(
                    "outcome",
                    outcome.ToString()));
        }
        if (result.Claimed == 0)
        {
            EffectOutcomes.Add(
                ZeroSentinel,
                new KeyValuePair<
                    string,
                    object?>(
                    "outcome",
                    "idle"));
        }
    }
}
