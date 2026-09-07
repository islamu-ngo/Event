namespace Explore.Infrastructure;

public sealed class AiRetentionCleanupSettings
{
    public const string SectionName = "AiRetentionCleanup";

    public bool Enabled { get; set; } = true;

    public bool DryRun { get; set; }

    public int InitialDelaySeconds { get; set; } = 30;

    public int PollingIntervalMinutes { get; set; } = 60;

    public int MaxTenantsPerPass { get; set; } = 100;
}
