namespace Explore.Infrastructure;

public sealed class IdempotencyCleanupSettings
{
    public const string SectionName = "IdempotencyCleanup";

    public bool Enabled { get; set; } = true;
    public bool DryRun { get; set; }
    public int InitialDelaySeconds { get; set; } = 30;
    public int PollingIntervalMinutes { get; set; } = 60;
    public int BatchSize { get; set; } = 500;
    public int ExpirationGraceHours { get; set; } = 24;
}
