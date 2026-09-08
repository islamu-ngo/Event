namespace Explore.Infrastructure.Waitlist;

public sealed class FairReturnOrchestrationDrainSettings
{
    public const string SectionName =
        "FairReturn:OrchestrationDrain";
    public const int MaximumBatchSize = 10_000;

    public int BatchSize { get; set; } = 100;
    public int MaximumEffectsPerTenant { get; set; } = 10;
    public int LeaseDurationSeconds { get; set; } = 120;
}
