namespace Explore.Infrastructure;

public sealed class EmailDispatchRetentionSettings
{
    public const string SectionName = "EmailDispatchRetention";

    public bool Enabled { get; set; } = true;
    public bool DryRun { get; set; }
    public int InitialDelaySeconds { get; set; } = 60;
    public int PollingIntervalMinutes { get; set; } = 60;
    public int MaxTenantsPerPass { get; set; } = 100;
    public int BatchSize { get; set; } = 500;
    public int RetentionDays { get; set; } = 180;
}
